using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Downloads;
using Groovefin.Core.Formatting;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// Settings → Downloads (#327): which collections are kept offline, in which format, on metered connections or
/// not; the status of the downloads; Sync now and Remove all. Create it per visit and dispose it.
/// </summary>
public sealed partial class DownloadsViewModel : ObservableObject, IDisposable
{
    /// <summary>The format choices: the original, then AAC from high to low.</summary>
    private static readonly int?[] Formats = [null, 320, 256, 192, 128];

    private readonly DownloadManager _downloads;
    private readonly ISettingsService _settings;
    private readonly INetworkStatus _network;
    private readonly IDialogService _dialogs;
    private readonly IStringResources _strings;
    private readonly IDispatcher _dispatcher;

    public DownloadsViewModel(
        DownloadManager downloads,
        ISettingsService settings,
        INetworkStatus network,
        IDialogService dialogs,
        IStringResources strings,
        IDispatcher dispatcher)
    {
        _downloads = downloads;
        _settings = settings;
        _network = network;
        _dialogs = dialogs;
        _strings = strings;
        _dispatcher = dispatcher;
        FormatOptions =
        [
            .. Formats.Select(kbps => kbps is { } value
                ? strings.GetString("Downloads_FormatAac").Replace("{0}", value.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
                : strings.GetString("Downloads_FormatOriginal")),
        ];

        _downloads.StatusChanged += OnStatusChanged;
        _settings.Changed += OnSettingsChanged;
        _network.Changed += OnNetworkChanged;
        Update(_downloads.Status);
    }

    public bool FavouriteSongs
    {
        get => Current.FavouriteSongs;
        set => Change(value != FavouriteSongs, d => d with { FavouriteSongs = value });
    }

    public bool Playlists
    {
        get => Current.Playlists;
        set => Change(value != Playlists, d => d with { Playlists = value });
    }

    public IReadOnlyList<string> FormatOptions { get; }

    /// <summary>Gets or sets the format as an index into <see cref="FormatOptions"/>.</summary>
    public int FormatIndex
    {
        get => Current.Format == DownloadFormatKind.Aac ? Math.Max(1, Array.IndexOf(Formats, Current.AacBitrateKbps)) : 0;
        set
        {
            if (value >= 0 && value < Formats.Length && value != FormatIndex)
            {
                Change(true, d => Formats[value] is { } kbps
                    ? d with { Format = DownloadFormatKind.Aac, AacBitrateKbps = kbps }
                    : d with { Format = DownloadFormatKind.Original });
            }
        }
    }

    public bool AllowMetered
    {
        get => Current.AllowMetered;
        set => Change(value != AllowMetered, d => d with { AllowMetered = value });
    }

    /// <summary>Gets e.g. "1,204 of 1,250 songs downloaded · 9.8 GB".</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    /// <summary>Gets what happens now, or why nothing does: "Downloading “Digital Love”", "Waiting for a network connection."…</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivity))]
    public partial string ActivityText { get; private set; } = string.Empty;

    public bool HasActivity => ActivityText.Length > 0;

    /// <summary>Gets the songs that failed, with the reason.</summary>
    public ObservableCollection<string> Failures { get; } = [];

    [ObservableProperty]
    public partial bool HasFailures { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    public partial bool IsSyncing { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveAllCommand))]
    public partial bool HasDownloads { get; private set; }

    private DownloadSettings Current => _settings.Current.Downloads;

    /// <summary>Shows what is on disk at once, before the next sync.</summary>
    public Task LoadAsync() => _downloads.RefreshStatusAsync();

    public void Dispose()
    {
        _downloads.StatusChanged -= OnStatusChanged;
        _settings.Changed -= OnSettingsChanged;
        _network.Changed -= OnNetworkChanged;
    }

    private bool CanSyncNow() => Current.AnyCollection && !IsSyncing && _network.IsOnline;

    /// <summary>Syncs at once; on a metered connection with downloads there off, it asks first.</summary>
    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    private async Task SyncNowAsync()
    {
        var allowMetered = false;
        if (_network.IsMetered && !Current.AllowMetered)
        {
            if (!await _dialogs.ConfirmAsync(
                    _strings.GetString("Downloads_MeteredTitle"),
                    _strings.GetString("Downloads_MeteredMessage"),
                    _strings.GetString("Downloads_MeteredConfirm"),
                    _strings.GetString("Dialog_Cancel")))
            {
                return;
            }

            allowMetered = true;
        }

        IsSyncing = true;
        await _downloads.SyncNowAsync(allowMetered);
    }

    /// <summary>Deletes every download after a confirmation, and turns the switches off, so nothing downloads again.</summary>
    [RelayCommand(CanExecute = nameof(HasDownloads))]
    private async Task RemoveAllAsync()
    {
        var size = ByteSizes.Format(_downloads.Status.Bytes, _strings);
        if (!await _dialogs.ConfirmAsync(
                _strings.GetString("Downloads_RemoveTitle"),
                _strings.GetString("Downloads_RemoveMessage").Replace("{0}", size, StringComparison.Ordinal),
                _strings.GetString("Downloads_RemoveConfirm"),
                _strings.GetString("Dialog_Cancel")))
        {
            return;
        }

        _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteSongs = false, Playlists = false } });
        await _downloads.RemoveAllAsync();
    }

    private void Change(bool changed, Func<DownloadSettings, DownloadSettings> change)
    {
        if (changed)
        {
            _settings.Update(s => s with { Downloads = change(s.Downloads) });
        }
    }

    private void OnStatusChanged(object? sender, EventArgs e) => _ = _dispatcher.EnqueueAsync(() => Update(_downloads.Status));

    private void OnNetworkChanged(object? sender, EventArgs e) => _ = _dispatcher.EnqueueAsync(SyncNowCommand.NotifyCanExecuteChanged);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Downloads == e.NewSettings.Downloads)
        {
            return;
        }

        OnPropertyChanged(nameof(FavouriteSongs));
        OnPropertyChanged(nameof(Playlists));
        OnPropertyChanged(nameof(FormatIndex));
        OnPropertyChanged(nameof(AllowMetered));
        SyncNowCommand.NotifyCanExecuteChanged();
    }

    private void Update(DownloadStatus status)
    {
        IsSyncing = status.IsSyncing;
        HasDownloads = status.Bytes > 0;
        StatusText = status.Wanted == 0 && status.Bytes == 0
            ? _strings.GetString("Downloads_StatusNone")
            : _strings.GetString("Downloads_Status")
                .Replace("{0}", status.Downloaded.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{1}", status.Wanted.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{2}", ByteSizes.Format(status.Bytes, _strings), StringComparison.Ordinal);
        ActivityText = status switch
        {
            { Current: { } name } => _strings.GetString("Downloads_Current").Replace("{0}", name, StringComparison.Ordinal),
            { IsSyncing: true } => _strings.GetString("Downloads_Checking"),
            { Stopped: DownloadStop.None } => string.Empty,
            { Stopped: var stop } when !Current.AnyCollection && stop != DownloadStop.LowDisk => string.Empty,
            { Stopped: var stop } => _strings.GetString($"Downloads_Stop{stop}"),
        };

        Failures.Clear();
        foreach (var failure in status.Failures)
        {
            var key = failure.Attempts >= DownloadManager.MaxAttempts ? "Downloads_FailureGivenUp" : "Downloads_Failure";
            Failures.Add(_strings.GetString(key)
                .Replace("{0}", failure.TrackName, StringComparison.Ordinal)
                .Replace("{1}", failure.Reason, StringComparison.Ordinal));
        }

        HasFailures = Failures.Count > 0;
    }
}
