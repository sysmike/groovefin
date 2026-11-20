using CommunityToolkit.Mvvm.ComponentModel;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Playback;

/// <summary>
/// The stream quality of the playing song (#397), for the now playing bar and the full-screen player. Its file's
/// details are asked for once when it starts; the streaming settings decide whether the server converts it.
/// Lives as long as the app; use it from the UI thread.
/// </summary>
public sealed partial class StreamQualityMonitor : ObservableObject, IDisposable
{
    private readonly IPlaybackEngine _engine;
    private readonly ILibraryService _library;
    private readonly IJellyfinClient _client;
    private readonly ISettingsService _settings;
    private readonly IStringResources _strings;
    private readonly IDispatcher? _dispatcher;
    private readonly IHomeNetwork? _home;
    private readonly StreamConversions? _conversions;
    private (string TrackId, TrackDetails Details)? _file;
    private string? _trackId;
    private bool _downloaded;
    private int _load;

    public StreamQualityMonitor(
        IPlaybackEngine engine,
        ILibraryService library,
        IJellyfinClient client,
        ISettingsService settings,
        IStringResources strings,
        IDispatcher? dispatcher = null,
        IHomeNetwork? home = null,
        StreamConversions? conversions = null)
    {
        _engine = engine;
        _library = library;
        _client = client;
        _settings = settings;
        _strings = strings;
        _dispatcher = dispatcher;
        _home = home;
        _conversions = conversions;
        _engine.CurrentChanged += OnCurrentChanged;
        _settings.Changed += OnSettingsChanged;
        if (conversions is not null)
        {
            conversions.Changed += OnConversionsChanged;
        }
    }

    /// <summary>Gets the label, e.g. "AAC 320 kbps · converted", or empty while nothing plays or it is not known.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLabel))]
    public partial string Label { get; private set; } = string.Empty;

    /// <summary>Gets why it plays so, for the label's tooltip.</summary>
    [ObservableProperty]
    public partial string Explanation { get; private set; } = string.Empty;

    public bool HasLabel => Label.Length > 0;

    public void Dispose()
    {
        _engine.CurrentChanged -= OnCurrentChanged;
        _settings.Changed -= OnSettingsChanged;
        if (_conversions is not null)
        {
            _conversions.Changed -= OnConversionsChanged;
        }
    }

    /// <summary>Gets the work of the last change, for tests.</summary>
    internal Task LastUpdate { get; private set; } = Task.CompletedTask;

    private void OnCurrentChanged(object? sender, EventArgs e) => LastUpdate = UpdateAsync();

    /// <summary>A song now streams converted: the label says so when it is the one playing.</summary>
    private void OnConversionsChanged(object? sender, EventArgs e) => OnUiThread(Show);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.NewSettings.Streaming != e.OldSettings.Streaming)
        {
            OnUiThread(Show);
        }
    }

    private async Task UpdateAsync()
    {
        var load = ++_load;
        var source = _engine.Current;
        _trackId = source?.Track.Id;
        _downloaded = source?.Url.IsFile == true;
        if (source is null)
        {
            Show();
            return;
        }

        if (_file?.TrackId != source.Track.Id)
        {
            _file = null;
            Show();
            try
            {
                var details = await _library.GetTrackDetailsAsync(source.Track.Id);
                if (load != _load)
                {
                    return;
                }

                _file = (source.Track.Id, details);
            }
            catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
            {
                // Offline or not reachable: without the file's details only a download can be named.
            }
        }

        if (load == _load)
        {
            OnUiThread(Show);
        }
    }

    private void Show()
    {
        if (_trackId is null)
        {
            Label = string.Empty;
            Explanation = string.Empty;
            return;
        }

        if (_file is not { } file || file.TrackId != _trackId)
        {
            Label = _downloaded ? _strings.GetString("StreamQuality_DownloadedFile") : string.Empty;
            Explanation = _downloaded ? _strings.GetString("StreamQuality_DownloadedWhy") : string.Empty;
            return;
        }

        var server = _client.Connection?.ServerUrl ?? new Uri("http://localhost/");
        var atHome = _home?.IsAtHome(server) ?? NetworkLocation.IsLocal(server);
        var options = _settings.Current.Streaming.OptionsFor(atHome);
        options = _conversions?.For(file.TrackId, options) ?? options;
        var info = StreamQuality.Describe(file.Details, options, atHome, _downloaded, _strings);
        Label = info.Label;
        Explanation = info.Explanation;
    }

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
}
