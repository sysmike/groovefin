using System.IO.Pipes;
using System.Text.Json;
using Groovefin.Core.Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

namespace Groovefin.Core.Tests.Discord;

/// <summary>Against a stand-in for the Discord client on a real named pipe of its own name.</summary>
public sealed class DiscordIpcConnectionTests : IAsyncDisposable
{
    private const string Ready = """{"cmd":"DISPATCH","evt":"READY","data":{"v":1}}""";

    private readonly FakeLogger<DiscordIpcConnection> _logger = new();
    private readonly string _pipeName = $"groovefin-test-{Guid.NewGuid():N}";
    private readonly NamedPipeServerStream _server;
    private readonly DiscordIpcConnection _sut;

    public DiscordIpcConnectionTests()
    {
        _server = new NamedPipeServerStream($"{_pipeName}-0", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _sut = new DiscordIpcConnection(_logger) { PipeName = _pipeName, PipeTimeout = TimeSpan.FromMilliseconds(50) };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        _sut.Dispose();
        await _server.DisposeAsync();
    }

    /// <summary>Accepts the client, checks the handshake and answers with <paramref name="reply"/>.</summary>
    private async Task<string> AcceptAsync(DiscordOpcode replyOpcode = DiscordOpcode.Frame, string reply = Ready)
    {
        await _server.WaitForConnectionAsync(Ct);
        var (opcode, json) = await DiscordFrame.ReadAsync(_server, Ct);
        opcode.Should().Be(DiscordOpcode.Handshake);
        await _server.WriteAsync(DiscordFrame.Encode(replyOpcode, reply), Ct);
        return json;
    }

    [Fact]
    public async Task Connecting_sends_the_handshake_and_waits_for_ready()
    {
        var server = AcceptAsync();

        await _sut.ConnectAsync("1234", Ct);

        JsonDocument.Parse(await server).RootElement.GetProperty("client_id").GetString().Should().Be("1234");
        _sut.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task The_activity_goes_out_as_a_frame()
    {
        var server = AcceptAsync();
        await _sut.ConnectAsync("1234", Ct);
        await server;

        await _sut.SetActivityAsync(new DiscordActivity("Intro", "Artist", null, null, null), Ct);
        var (opcode, json) = await DiscordFrame.ReadAsync(_server, Ct);

        opcode.Should().Be(DiscordOpcode.Frame);
        JsonDocument.Parse(json).RootElement.GetProperty("args").GetProperty("activity").GetProperty("details").GetString().Should().Be("Intro");
    }

    [Fact]
    public async Task A_ping_is_answered_with_a_pong()
    {
        var server = AcceptAsync();
        await _sut.ConnectAsync("1234", Ct);
        await server;

        await _server.WriteAsync(DiscordFrame.Encode(DiscordOpcode.Ping, "{\"n\":7}"), Ct);
        var (opcode, json) = await DiscordFrame.ReadAsync(_server, Ct);

        opcode.Should().Be(DiscordOpcode.Pong);
        json.Should().Be("{\"n\":7}");
    }

    [Fact]
    public async Task A_rejected_activity_is_logged_and_the_connection_stays()
    {
        var server = AcceptAsync();
        await _sut.ConnectAsync("1234", Ct);
        await server;

        await _server.WriteAsync(DiscordFrame.Encode(DiscordOpcode.Frame, """{"cmd":"SET_ACTIVITY","evt":"ERROR","data":{"code":4000,"message":"child \"activity\" fails"}}"""), Ct);
        for (var i = 0; i < 100 && _logger.Collector.Count == 1; i++)
        {
            await Task.Delay(20, Ct);
        }

        _logger.LatestRecord.Level.Should().Be(LogLevel.Warning);
        _logger.LatestRecord.Message.Should().Contain("child");
        _sut.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task A_refused_handshake_fails_to_connect()
    {
        var server = AcceptAsync(DiscordOpcode.Close, """{"code":4000,"message":"Invalid Client ID"}""");

        var act = () => _sut.ConnectAsync("wrong", Ct);

        await act.Should().ThrowAsync<IOException>();
        await server;
        _sut.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Without_Discord_connecting_fails_with_an_io_error()
    {
        var sut = new DiscordIpcConnection(NullLogger<DiscordIpcConnection>.Instance) { PipeName = $"groovefin-none-{Guid.NewGuid():N}", PipeTimeout = TimeSpan.FromMilliseconds(20) };

        var act = () => sut.ConnectAsync("1234", Ct);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task When_Discord_quits_the_connection_notices()
    {
        var server = AcceptAsync();
        await _sut.ConnectAsync("1234", Ct);
        await server;

        _server.Disconnect();
        for (var i = 0; i < 100 && _sut.IsConnected; i++)
        {
            await Task.Delay(20, Ct);
        }

        _sut.IsConnected.Should().BeFalse();
    }
}
