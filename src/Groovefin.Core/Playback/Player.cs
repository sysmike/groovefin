using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Queue;
using Groovefin.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Playback;

/// <summary>
/// <see cref="IPlayer"/>: keeps the engine on the queue's current entry. Builds stream URLs with the
/// streaming settings for the active server, moves on when a track ends, and skips tracks that fail
/// (up to <see cref="MaxConsecutiveFailures"/> in a row, then stops). Use it from the UI thread.
/// </summary>
public sealed partial class Player : IPlayer, IDisposable
{
    /// <summary>After this many failing tracks in a row playback stops instead of skipping on.</summary>
    public const int MaxConsecutiveFailures = 3;

    /// <summary>Previous restarts the track when it has played longer than this.</summary>
    public static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    private readonly IStreamUrlProvider _streams;
    private readonly IJellyfinClient _client;
    private readonly ISettingsService _settings;
    private readonly IErrorReporter _errors;
    private readonly ILogger<Player> _logger;
    private readonly Random _random;
    private string? _loadedItemId;
    private int _failures;
    private bool _driving;

    public Player(
        QueueManager queue,
        IPlaybackEngine engine,
        IStreamUrlProvider streams,
        IJellyfinClient client,
        ISettingsService settings,
        IErrorReporter errors,
        ILogger<Player> logger,
        Random? random = null)
    {
        Queue = queue;
        Engine = engine;
        _streams = streams;
        _client = client;
        _settings = settings;
        _errors = errors;
        _logger = logger;
        _random = random ?? Random.Shared;

        Engine.Ended += OnEnded;
        Engine.Failed += OnFailed;
        Queue.Changed += OnQueueChanged;
    }

    public QueueManager Queue { get; }

    public IPlaybackEngine Engine { get; }

    public void Play(IReadOnlyList<Track> tracks, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0)
        {
            return;
        }

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

        Drive(() =>
        {
            Queue.Shuffle = true;
            Queue.PlayNow(tracks, _random.Next(tracks.Count));
        });
        LoadCurrent();
    }

    public void PlayNext(IReadOnlyList<Track> tracks) => Drive(() => Queue.PlayNext(tracks));

    public void AddToQueue(IReadOnlyList<Track> tracks) => Drive(() => Queue.AddLast(tracks));

    public void TogglePlayPause()
    {
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
        _failures = 0;
        Advance(() => Queue.Next());
    }

    public void Previous()
    {
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
        Engine.Ended -= OnEnded;
        Engine.Failed -= OnFailed;
        Queue.Changed -= OnQueueChanged;
    }

    private void OnEnded(object? sender, EventArgs e)
    {
        _failures = 0;
        Advance(() => Queue.TrackEnded());
    }

    private void OnFailed(object? sender, PlaybackFailedEventArgs e)
    {
        LogPlaybackFailed(e.Exception, e.Source.ToString(), e.Message);
        _errors.Report(e.Exception ?? new InvalidOperationException(e.Message), Operations.Playback);

        if (++_failures >= MaxConsecutiveFailures)
        {
            _failures = 0;
            Stop();
            return;
        }

        Advance(() => Queue.Next());
    }

    /// <summary>Follows changes made by others (e.g. the queue panel removed the playing entry).</summary>
    private void OnQueueChanged(object? sender, EventArgs e)
    {
        if (_driving || Engine.Current is null || Queue.Current?.Id == _loadedItemId)
        {
            return;
        }

        if (Queue.Current is null)
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

    private void LoadCurrent(bool autoPlay = true)
    {
        if (Queue.Current is not { } item)
        {
            Stop();
            return;
        }

        try
        {
            var server = _client.Connection?.ServerUrl ?? throw new InvalidOperationException("Not connected to a server.");
            var url = _streams.GetStreamUrl(item.Track.Id, _settings.Current.Streaming.OptionsFor(server));
            _loadedItemId = item.Id;
            Engine.Load(new PlaybackSource(item.Id, url, item.Track), autoPlay);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _errors.Report(ex, Operations.Playback);
            Stop();
        }
    }

    private void Stop()
    {
        _loadedItemId = null;
        Engine.Stop();
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Playback of {Source} failed: {Message}")]
    private partial void LogPlaybackFailed(Exception? exception, string source, string message);
}
