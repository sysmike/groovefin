using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Discord;

/// <summary>A connection to the Discord client running on this PC.</summary>
public interface IDiscordConnection : IDisposable
{
    /// <summary>Gets a value indicating whether the handshake succeeded and Discord has not closed the pipe since.</summary>
    bool IsConnected { get; }

    /// <summary>Connects to the running Discord client as <paramref name="applicationId"/>.</summary>
    /// <exception cref="IOException">Discord is not running or refused the handshake.</exception>
    Task ConnectAsync(string applicationId, CancellationToken cancellationToken = default);

    /// <summary>Sets the activity shown in the user's status; <see langword="null"/> clears it.</summary>
    /// <exception cref="IOException">The pipe broke (Discord quit).</exception>
    Task SetActivityAsync(DiscordActivity? activity, CancellationToken cancellationToken = default);

    /// <summary>Closes the pipe; Discord then removes the activity.</summary>
    void Disconnect();
}

/// <summary>
/// Discord's local RPC (no token, no network): the Discord client listens on the named pipes
/// <c>discord-ipc-0</c> to <c>discord-ipc-9</c>. A handshake with the application ID, then
/// <c>SET_ACTIVITY</c> messages. A read loop answers pings and notices when Discord closes the pipe.
/// </summary>
public sealed partial class DiscordIpcConnection(ILogger<DiscordIpcConnection> logger) : IDiscordConnection
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _reading;

    /// <summary>Gets or sets the pipe name without the number (tests use their own).</summary>
    public string PipeName { get; init; } = "discord-ipc";

    /// <summary>Gets or sets how long to wait for each pipe; Discord answers at once when it runs.</summary>
    public TimeSpan PipeTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(applicationId);
        Disconnect();

        var pipe = await OpenPipeAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("Discord is not running.");
        try
        {
            await pipe.WriteAsync(DiscordFrame.Encode(DiscordOpcode.Handshake, DiscordMessages.Handshake(applicationId)), cancellationToken).ConfigureAwait(false);
            var (opcode, json) = await DiscordFrame.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (opcode != DiscordOpcode.Frame || !IsReady(json))
            {
                throw new IOException($"Discord refused the connection: {json}");
            }
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _pipe = pipe;
        _reading = new CancellationTokenSource();
        IsConnected = true;
        LogConnected();
        _ = ReadLoopAsync(pipe, _reading.Token);
    }

    public async Task SetActivityAsync(DiscordActivity? activity, CancellationToken cancellationToken = default)
    {
        var pipe = _pipe;
        if (pipe is null || !IsConnected)
        {
            throw new IOException("Not connected to Discord.");
        }

        var message = DiscordMessages.SetActivity(Environment.ProcessId, activity, Guid.NewGuid().ToString("N"));
        await WriteAsync(pipe, DiscordFrame.Encode(DiscordOpcode.Frame, message), cancellationToken).ConfigureAwait(false);
    }

    public void Disconnect()
    {
        IsConnected = false;
        _reading?.Cancel();
        _reading?.Dispose();
        _reading = null;
        _pipe?.Dispose();
        _pipe = null;
    }

    public void Dispose()
    {
        Disconnect();
        _writeLock.Dispose();
    }

    private static bool IsReady(string json) => HasEvent(json, "READY");

    /// <summary>Discord answers a message it does not accept with the event <c>ERROR</c>.</summary>
    private static bool IsError(string json) => HasEvent(json, "ERROR");

    private static bool HasEvent(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("evt", out var evt) && evt.ValueKind == JsonValueKind.String && evt.GetString() == name;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<NamedPipeClientStream?> OpenPipeAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"{PipeName}-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(PipeTimeout, cancellationToken).ConfigureAwait(false);
                return pipe;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }

        return null;
    }

    private async Task WriteAsync(Stream pipe, byte[] frame, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await pipe.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            throw new IOException("The Discord pipe is closed.", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Discord answers every message; reading them keeps the pipe from filling up.</summary>
    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var (opcode, json) = await DiscordFrame.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (opcode == DiscordOpcode.Ping)
                {
                    await WriteAsync(pipe, DiscordFrame.Encode(DiscordOpcode.Pong, json), cancellationToken).ConfigureAwait(false);
                }
                else if (opcode == DiscordOpcode.Frame && IsError(json))
                {
                    LogRejected(json);
                }
                else if (opcode == DiscordOpcode.Close)
                {
                    LogClosed(json);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disconnect.
            return;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException or ObjectDisposedException)
        {
            LogClosed(ex.Message);
        }

        if (ReferenceEquals(pipe, _pipe))
        {
            IsConnected = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to Discord")]
    private partial void LogConnected();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discord closed the connection: {Reason}")]
    private partial void LogClosed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discord rejected a message: {Reply}")]
    private partial void LogRejected(string reply);
}
