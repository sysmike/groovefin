using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Queue;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Playback;

/// <summary>
/// <see cref="IPlayer"/>: keeps the engine on the queue's current entry. Builds stream URLs with the
/// streaming settings for the active server, moves on when a track ends, and skips tracks that fail
/// (up to <see cref="MaxConsecutiveFailures"/> in a row, then stops). The entry after the current one is
/// queued in the engine (<see cref="IPlaybackEngine.SetNext"/>) so it follows without a gap; when the
/// engine moves on by itself, the queue follows. A downloaded song plays from its file, also when the server is
/// reachable; offline, the songs that are not downloaded are skipped without a try (#333). A stream the network
/// broke is tried again where it stopped, after <see cref="RetryDelays"/> and at once when the network is back,
/// before it counts as a failure (#385). Use it from the UI thread.
/// </summary>
public sealed partial class Player : IPlayer, IDisposable
{
    /// <summary>After this many failing tracks in a row playback stops instead of skipping on.</summary>
    public const int MaxConsecutiveFailures = 3;

    /// <summary>Previous restarts the track when it has played longer than this.</summary>
    public static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The waits before each new try of a stream the network broke (#385): about 30 seconds in all. After the last
    /// one the track counts as failed and is skipped.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15)];

    private readonly IStreamUrlProvider _streams;
    private readonly IJellyfinClient _client;
    private readonly ISettingsService _settings;
    private readonly IErrorReporter _errors;
    private readonly ILogger<Player> _logger;
    private readonly IOfflineLibrary? _offline;
    private readonly INetworkStatus? _network;
    private readonly TimeProvider _time;
    private readonly IDispatcher? _dispatcher;
    private string? _loadedItemId;
    private Reconnection? _reconnection;
    private bool _wasOnline;
    private int _failures;
    private bool _driving;
    private bool _loading;

    public Player(
        QueueManager queue,
        IPlaybackEngine engine,
        IStreamUrlProvider streams,
        IJellyfinClient client,
        ISettingsService settings,
        IErrorReporter errors,
        ILogger<Player> logger,
        IOfflineLibrary? offline = null,
        INetworkStatus? network = null,
        TimeProvider? time = null,
        IDispatcher? dispatcher = null)
    {
        Queue = queue;
        Engine = engine;
        _streams = streams;
        _client = client;
        _settings = settings;
        _errors = errors;
        _logger = logger;
        _offline = offline;
        _network = network;
        _time = time ?? TimeProvider.System;
        _dispatcher = dispatcher;
        _wasOnline = network?.IsOnline ?? true;

        Engine.Ended += OnEnded;
        Engine.StateChanged += OnEngineStateChanged;
        Engine.Failed += OnFailed;
        Engine.CurrentChanged += OnEngineCurrentChanged;
        Queue.Changed += OnQueueChanged;
        _settings.Changed += OnSettingsChanged;
        if (_network is not null)
        {
            _network.Changed += OnNetworkChanged;
        }

        Engine.Crossfade = CrossfadeFrom(settings.Current.Playback);
    }

    /// <summary>Raised when <see cref="IsReconnecting"/> changed.</summary>
    public event EventHandler? ReconnectingChanged;

    public QueueManager Queue { get; }

    public IPlaybackEngine Engine { get; }

    /// <summary>Gets a value indicating whether a stream the network broke waits to be tried again (#385).</summary>
    public bool IsReconnecting => _reconnection is not null;

    public void Play(IReadOnlyList<Track> tracks, int? startIndex = null)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0)
        {
            return;
        }

        EndReconnection();
        Drive(() => Queue.PlayNow(tracks, startIndex));
        LoadCurrent();
    }

    public void Shuffle(IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0)
        {
            return;
        }

        EndReconnection();
        Drive(() =>
        {
            Queue.Shuffle = true;
            Queue.PlayNow(tracks);
        });
        LoadCurrent();
    }

    public void ShuffleAlbums(IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0)
        {
            return;
        }

        EndReconnection();
        Drive(() => Queue.PlayAlbumsShuffled(tracks));
        LoadCurrent();
    }

    public void PlayNext(IReadOnlyList<Track> tracks)
    {
        Drive(() => Queue.PlayNext(tracks));
        UpdateNext();
    }

    public void AddToQueue(IReadOnlyList<Track> tracks)
    {
        Drive(() => Queue.AddLast(tracks));
        UpdateNext();
    }

    public void InsertIntoQueue(IReadOnlyList<Track> tracks, int position)
    {
        Drive(() => Queue.InsertUpcoming(tracks, position));
        UpdateNext();
    }

    public void TogglePlayPause()
    {
        if (_reconnection is not null)
        {
            // Nothing plays while it waits: Play tries again now.
            RetryNow();
            return;
        }

        if (Engine.State is PlaybackState.Playing or PlaybackState.Buffering or PlaybackState.Opening)
        {
            Engine.Pause();
        }
        else if (Engine.Current is not null)
        {
            Engine.Play();
        }
        else if (Queue.Current is not null)
        {
            // Stopped at the end of the queue, or nothing loaded yet: start the current entry again.
            LoadCurrent();
        }
    }

    public void Next()
    {
        EndReconnection();
        _failures = 0;
        Advance(() => Queue.Next());
    }

    public void Previous()
    {
        EndReconnection();
        if (Engine.Current is not null && Engine.Position > RestartThreshold)
        {
            Engine.Seek(TimeSpan.Zero);
            return;
        }

        var before = Queue.Current;
        Drive(() => Queue.Previous());
        if (Queue.Current is not null && ReferenceEquals(before, Queue.Current))
        {
            Engine.Seek(TimeSpan.Zero);
        }
        else
        {
            LoadCurrent();
        }
    }

    public void JumpTo(string queueItemId)
    {
        EndReconnection();
        _failures = 0;
        var jumped = false;
        Drive(() => jumped = Queue.JumpTo(queueItemId));
        if (jumped)
        {
            LoadCurrent();
        }
    }

    public void Dispose()
    {
        _reconnection?.Timer.Dispose();
        _reconnection = null;
        Engine.Ended -= OnEnded;
        Engine.StateChanged -= OnEngineStateChanged;
        if (_network is not null)
        {
            _network.Changed -= OnNetworkChanged;
        }

        Engine.Failed -= OnFailed;
        Engine.CurrentChanged -= OnEngineCurrentChanged;
        Queue.Changed -= OnQueueChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    private static TimeSpan CrossfadeFrom(PlaybackSettings playback) => TimeSpan.FromSeconds(Math.Max(0, playback.CrossfadeSeconds));

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Engine.Crossfade = CrossfadeFrom(e.NewSettings.Playback);

    private void OnEnded(object? sender, EventArgs e)
    {
        _failures = 0;
        Advance(() => Queue.TrackEnded());
    }

    private void OnFailed(object? sender, PlaybackFailedEventArgs e)
    {
        LogPlaybackFailed(e.Exception, e.Source.ToString(), e.Message);
        // Only a stream: a downloaded file does not need the network.
        if ((e.IsNetworkError || _network is { IsOnline: false }) && e.Source.UrlAt is not null && TryReconnect(e.Source))
        {
            return;
        }

        EndReconnection();
        CountFailure(new PlaybackException(e.Message, e.Exception));
    }

    /// <summary>Reports a track that can't play and skips it, or stops after <see cref="MaxConsecutiveFailures"/> in a row.</summary>
    private void CountFailure(Exception error)
    {
        _errors.Report(error, Operations.Playback);
        if (++_failures >= MaxConsecutiveFailures)
        {
            _failures = 0;
            Stop();
            return;
        }

        Advance(() => Queue.Next());
    }

    /// <summary>The engine moved on to the queued next entry by itself (gapless): the queue follows.</summary>
    private void OnEngineCurrentChanged(object? sender, EventArgs e)
    {
        if (_loading || Engine.Current is not { } source || _loadedItemId is null)
        {
            // Our own load or stop, or a stop from outside (closing the app).
            return;
        }

        _failures = 0;
        QueueItem? next = null;
        Drive(() => next = Queue.TrackEnded());
        if (next?.Id == source.Id)
        {
            _loadedItemId = source.Id;
            UpdateNext();
        }
        else
        {
            // The queue changed while the engine moved on: play what it says now.
            LoadCurrent();
        }
    }

    /// <summary>Follows changes made by others (e.g. the queue panel removed the playing entry).</summary>
    private void OnQueueChanged(object? sender, EventArgs e)
    {
        if (_driving || Engine.Current is null)
        {
            return;
        }

        if (Queue.Current?.Id == _loadedItemId)
        {
            // Reordered, shuffled, repeat changed or entries added: the entry after this one may differ.
            UpdateNext();
        }
        else if (Queue.Current is null)
        {
            Stop();
        }
        else
        {
            LoadCurrent(autoPlay: Engine.State == PlaybackState.Playing);
        }
    }

    /// <summary>Moves the queue with <paramref name="move"/>; stops at the end of the queue.</summary>
    private void Advance(Func<QueueItem?> move)
    {
        QueueItem? next = null;
        Drive(() => next = move());
        if (next is null)
        {
            Stop();
        }
        else
        {
            LoadCurrent();
        }
    }

    public void Resume(TimeSpan position)
    {
        EndReconnection();
        LoadCurrent(autoPlay: false, position < TimeSpan.Zero ? TimeSpan.Zero : position);
    }

    private void LoadCurrent(bool autoPlay = true, TimeSpan startPosition = default)
    {
        if (Queue.Current is not { } item)
        {
            Stop();
            return;
        }

        if (_reconnection is { } waiting && waiting.ItemId != item.Id)
        {
            EndReconnection();
        }

        if (!CanPlay(item))
        {
            if (!SkipToPlayable())
            {
                _errors.Report(new NotDownloadedException(), Operations.Playback);
                Stop();
                return;
            }

            item = Queue.Current!;
            startPosition = TimeSpan.Zero;
        }

        try
        {
            var source = SourceFor(item) with { StartPosition = startPosition };
            _loadedItemId = item.Id;
            _loading = true;
            try
            {
                Engine.Load(source, autoPlay);
            }
            finally
            {
                _loading = false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _errors.Report(ex, Operations.Playback);
            Stop();
            return;
        }

        UpdateNext();
    }

    /// <summary>Queues the entry after the current one in the engine, unless it is queued already.</summary>
    private void UpdateNext()
    {
        if (Engine.Current is null)
        {
            return;
        }

        var item = Queue.PeekNext();
        if (item is not null && !CanPlay(item))
        {
            // Offline and not downloaded: the end of this one skips to the next that is (LoadCurrent).
            item = null;
        }

        if (item?.Id == Engine.Next?.Id)
        {
            return;
        }

        try
        {
            Engine.SetNext(item is null ? null : SourceFor(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Not gapless then: the next entry loads (and reports its error) when this one ends.
            LogNextFailed(ex, item!.Id);
            Engine.SetNext(null);
        }
    }

    /// <summary>Whether the entry can play: online every one can, offline only a downloaded one.</summary>
    private bool CanPlay(QueueItem item) => _network is not { IsOnline: false } || _offline?.FileOf(item.Track.Id) is not null;

    /// <summary>Offline: moves the queue on to the next downloaded entry; <see langword="false"/> when there is none.</summary>
    private bool SkipToPlayable()
    {
        for (var i = 0; i < Queue.Items.Count; i++)
        {
            QueueItem? next = null;
            Drive(() => next = Queue.Next());
            if (next is null)
            {
                return false;
            }

            if (CanPlay(next))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The song's downloaded file when there is one (seeking in it needs no new URL), else a stream.</summary>
    /// <exception cref="InvalidOperationException">Not downloaded, and not connected to a server.</exception>
    private PlaybackSource SourceFor(QueueItem item)
    {
        var gain = Normalization.VolumeFactor(item.Track, _settings.Current.Playback.Normalization);
        if (_offline?.FileOf(item.Track.Id) is { } file)
        {
            return new PlaybackSource(item.Id, new Uri(file), item.Track, gain);
        }

        var server = _client.Connection?.ServerUrl ?? throw new InvalidOperationException("Not connected to a server.");
        var options = _settings.Current.Streaming.OptionsFor(server);
        var url = _streams.GetStreamUrl(item.Track.Id, options);
        return new PlaybackSource(item.Id, url, item.Track, gain)
        {
            UrlAt = position => _streams.GetStreamUrl(item.Track.Id, options with { StartPosition = position }),
        };
    }

    private void Stop()
    {
        EndReconnection();
        _loadedItemId = null;
        Engine.Stop();
    }

    /// <summary>
    /// Waits to try <paramref name="source"/> again where it stopped, the next of <see cref="RetryDelays"/>.
    /// Returns <see langword="false"/> when it is not the queue's current entry or has been tried often enough.
    /// </summary>
    private bool TryReconnect(PlaybackSource source)
    {
        if (Queue.Current?.Id != source.Id)
        {
            return false;
        }

        var earlier = _reconnection?.ItemId == source.Id ? _reconnection : null;
        var attempt = earlier?.Attempt ?? 0;
        if (attempt >= RetryDelays.Count)
        {
            return false;
        }

        // A failed try fails at its start position, which is where the stream broke.
        var position = earlier?.Position ?? Engine.Position;
        _reconnection?.Timer.Dispose();
        var delay = RetryDelays[attempt];
        var timer = _time.CreateTimer(_ => OnUiThread(RetryNow), null, delay, Timeout.InfiniteTimeSpan);
        var wasReconnecting = _reconnection is not null;
        _reconnection = new Reconnection(source.Id, position, attempt + 1, timer);
        LogReconnecting(source.ToString(), position, attempt + 1, delay);
        if (!wasReconnecting)
        {
            ReconnectingChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>Tries the broken stream again now, from where it stopped.</summary>
    private void RetryNow()
    {
        if (_reconnection is not { } waiting)
        {
            return;
        }

        waiting.Timer.Dispose();
        if (Queue.Current is not { } item || item.Id != waiting.ItemId)
        {
            EndReconnection();
            return;
        }

        if (!CanPlay(item))
        {
            // Still offline: a load would skip to a downloaded song. Wait for the next try, or for the network.
            if (Engine.Current is not { } source || source.Id != item.Id || !TryReconnect(source))
            {
                EndReconnection();
                CountFailure(new PlaybackException("The network did not come back.", null));
            }

            return;
        }

        LoadCurrent(autoPlay: true, waiting.Position);
    }

    /// <summary>Stops waiting to try again: it played, the user moved on, or it was tried often enough.</summary>
    private void EndReconnection()
    {
        if (_reconnection is not { } waiting)
        {
            return;
        }

        waiting.Timer.Dispose();
        _reconnection = null;
        ReconnectingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A try that plays ends the waiting, and the failures before it no longer count.</summary>
    private void OnEngineStateChanged(object? sender, EventArgs e)
    {
        if (Engine.State == PlaybackState.Playing && _reconnection is { } waiting && Engine.Current?.Id == waiting.ItemId)
        {
            LogReconnected(Engine.Current.ToString(), waiting.Attempt);
            _failures = 0;
            EndReconnection();
        }
    }

    /// <summary>
    /// The network is back: a stream waiting to be tried again is tried now, and one stuck buffering since the
    /// network went away starts again where it was, which a stalled stream does not do by itself.
    /// </summary>
    private void OnNetworkChanged(object? sender, EventArgs e) => OnUiThread(() =>
    {
        var online = _network!.IsOnline;
        var cameBack = online && !_wasOnline;
        _wasOnline = online;
        if (!cameBack)
        {
            return;
        }

        if (_reconnection is not null)
        {
            RetryNow();
        }
        else if (Engine.State == PlaybackState.Buffering && Engine.Current is { UrlAt: not null } stalled && Queue.Current?.Id == stalled.Id)
        {
            LogStalledRestart(stalled.ToString(), Engine.Position);
            LoadCurrent(autoPlay: true, Engine.Position);
        }
    });

    private void OnUiThread(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _ = _dispatcher.EnqueueAsync(action);
        }
    }

    /// <summary>Runs a queue change of our own; <see cref="OnQueueChanged"/> ignores the events it raises.</summary>
    private void Drive(Action change)
    {
        _driving = true;
        try
        {
            change();
        }
        finally
        {
            _driving = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The network broke {Source} at {Position}; try {Attempt} in {Delay}")]
    private partial void LogReconnecting(string source, TimeSpan position, int attempt, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Source} plays again after {Attempts} tries")]
    private partial void LogReconnected(string source, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "The network is back: {Source} was stuck buffering and starts again at {Position}")]
    private partial void LogStalledRestart(string source, TimeSpan position);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Queueing {ItemId} for gapless playback failed")]
    private partial void LogNextFailed(Exception exception, string itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Playback of {Source} failed: {Message}")]
    private partial void LogPlaybackFailed(Exception? exception, string source, string message);

    /// <summary>A stream the network broke, waiting for its next try (#385).</summary>
    private sealed record Reconnection(string ItemId, TimeSpan Position, int Attempt, ITimer Timer);
}
