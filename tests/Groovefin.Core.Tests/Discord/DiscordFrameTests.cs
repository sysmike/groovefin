using System.Text.Json;
using Groovefin.Core.Discord;

namespace Groovefin.Core.Tests.Discord;

public sealed class DiscordFrameTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_frame_is_opcode_and_length_little_endian_then_the_json()
    {
        var frame = DiscordFrame.Encode(DiscordOpcode.Frame, "{}");

        frame.Should().Equal(1, 0, 0, 0, 2, 0, 0, 0, (byte)'{', (byte)'}');
    }

    [Fact]
    public async Task A_frame_reads_back()
    {
        using var stream = new MemoryStream(DiscordFrame.Encode(DiscordOpcode.Ping, "{\"x\":\"ü\"}"));

        var (opcode, json) = await DiscordFrame.ReadAsync(stream, Ct);

        opcode.Should().Be(DiscordOpcode.Ping);
        json.Should().Be("{\"x\":\"ü\"}");
    }

    [Fact]
    public async Task A_closed_pipe_ends_the_read()
    {
        using var stream = new MemoryStream([1, 0, 0]);

        var act = () => DiscordFrame.ReadAsync(stream, Ct);

        await act.Should().ThrowAsync<EndOfStreamException>();
    }

    [Fact]
    public async Task An_oversized_body_is_refused()
    {
        using var stream = new MemoryStream([1, 0, 0, 0, 0, 0, 0, 1]);

        var act = () => DiscordFrame.ReadAsync(stream, Ct);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void The_handshake_names_the_application()
    {
        using var json = JsonDocument.Parse(DiscordMessages.Handshake("1234"));

        json.RootElement.GetProperty("v").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("client_id").GetString().Should().Be("1234");
    }

    [Fact]
    public void An_activity_is_listening_with_text_progress_and_the_icon()
    {
        var start = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        var activity = new DiscordActivity("One More Time", "Daft Punk", "Discovery", start, start.AddMinutes(5));

        using var json = JsonDocument.Parse(DiscordMessages.SetActivity(42, activity, "n1"));

        var root = json.RootElement;
        root.GetProperty("cmd").GetString().Should().Be("SET_ACTIVITY");
        root.GetProperty("nonce").GetString().Should().Be("n1");
        root.GetProperty("args").GetProperty("pid").GetInt32().Should().Be(42);
        var a = root.GetProperty("args").GetProperty("activity");
        a.GetProperty("type").GetInt32().Should().Be(DiscordMessages.ListeningType);
        a.GetProperty("details").GetString().Should().Be("One More Time");
        a.GetProperty("state").GetString().Should().Be("Daft Punk");
        a.GetProperty("timestamps").GetProperty("start").GetInt64().Should().Be(1_700_000_000_000);
        a.GetProperty("timestamps").GetProperty("end").GetInt64().Should().Be(1_700_000_300_000);
        a.GetProperty("assets").GetProperty("large_image").GetString().Should().Be(DiscordMessages.IconAsset);
        a.GetProperty("assets").GetProperty("large_text").GetString().Should().Be("Discovery");
    }

    [Fact]
    public void A_paused_activity_has_no_progress_and_clearing_sends_none()
    {
        using var paused = JsonDocument.Parse(DiscordMessages.SetActivity(1, new DiscordActivity("Intro", null, null, null, null), "n"));
        using var cleared = JsonDocument.Parse(DiscordMessages.SetActivity(1, null, "n"));

        var activity = paused.RootElement.GetProperty("args").GetProperty("activity");
        activity.TryGetProperty("timestamps", out _).Should().BeFalse();
        activity.TryGetProperty("state", out _).Should().BeFalse();
        cleared.RootElement.GetProperty("args").TryGetProperty("activity", out _).Should().BeFalse();
    }

    [Fact]
    public void Text_is_cut_to_128_characters_and_padded_to_two()
    {
        DiscordMessages.Fit(new string('a', 200)).Should().HaveLength(128).And.EndWith("…");
        DiscordMessages.Fit("X").Should().HaveLength(2).And.StartWith("X");
        DiscordMessages.Fit("  Intro ").Should().Be("Intro");
    }
}
