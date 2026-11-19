using Groovefin.Core.Api;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Servers;

/// <summary>Whether the PC is in a server's home network, for the stream quality limits (#453).</summary>
public interface IHomeNetwork
{
    /// <summary>Gets a value indicating whether the PC is in the home network of the server at <paramref name="serverUrl"/>.</summary>
    bool IsAtHome(Uri serverUrl);
}

/// <summary>
/// Asks the signed-in server whether the PC is in its home network (#453): after each sign-in and each network
/// change, a moment later, once the new network works. Until it answers, or when it can't (an older server, no
/// network), the address rules of <see cref="NetworkLocation"/> apply. A server address alone doesn't tell: a
/// public name such as <c>jellyfin.example.com</c> is also used at home.
/// </summary>
public sealed partial class HomeNetwork : IHomeNetwork, IDisposable
{
    /// <summary>How long a new network gets to settle before the server is asked.</summary>
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    private readonly IJellyfinClient _client;
    private readonly IServerNetwork _server;
    private readonly INetworkStatus _network;
    private readonly TimeProvider _time;
    private readonly ILogger<HomeNetwork> _logger;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _check;
    private Answer? _answer;
    private bool _disposed;

    public HomeNetwork(IJellyfinClient client, IServerNetwork server, INetworkStatus network, ILogger<HomeNetwork> logger, TimeProvider? time = null)
    {
        _client = client;
        _server = server;
        _network = network;
        _logger = logger;
        _time = time ?? TimeProvider.System;

        client.ConnectionChanged += OnChanged;
        network.Changed += OnChanged;
        Check(TimeSpan.Zero);
    }

    /// <summary>Gets the last check, for tests.</summary>
    internal Task Checking { get; private set; } = Task.CompletedTask;

    public bool IsAtHome(Uri serverUrl)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);

        Answer? answer;
        lock (_gate)
        {
            answer = _answer;
        }

        return answer is not null && answer.ServerUrl == serverUrl ? answer.AtHome : NetworkLocation.IsLocal(serverUrl);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _check?.Cancel();
            _check?.Dispose();
            _check = null;
        }

        _client.ConnectionChanged -= OnChanged;
        _network.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Check(SettleDelay);

    /// <summary>Forgets the answer and asks again after <paramref name="delay"/>; a check still running stops.</summary>
    private void Check(TimeSpan delay)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // The running check took its token at its start, so its source can go.
            _check?.Cancel();
            _check?.Dispose();
            _check = new CancellationTokenSource();
            token = _check.Token;
            _answer = null;
        }

        Checking = AskAsync(delay, token);
    }

    private async Task AskAsync(TimeSpan delay, CancellationToken token)
    {
        if (_client.Connection is not { } connection || !_network.IsOnline)
        {
            return;
        }

        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _time, token).ConfigureAwait(false);
            }

            var atHome = await _server.IsInServerNetworkAsync(token).ConfigureAwait(false);
            lock (_gate)
            {
                if (token.IsCancellationRequested || atHome is not { } value || !Equals(_client.Connection, connection))
                {
                    return;
                }

                _answer = new Answer(connection.ServerUrl, value);
            }

            LogAnswer(atHome.Value);
        }
        catch (OperationCanceledException)
        {
            // The network or the server changed again; that check asks.
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or HttpRequestException)
        {
            LogNoAnswer(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The server says this PC is in its home network: {AtHome}")]
    private partial void LogAnswer(bool atHome);

    [LoggerMessage(Level = LogLevel.Information, Message = "The server didn't say whether this PC is in its home network; its address decides")]
    private partial void LogNoAnswer(Exception exception);

    private sealed record Answer(Uri ServerUrl, bool AtHome);
}
