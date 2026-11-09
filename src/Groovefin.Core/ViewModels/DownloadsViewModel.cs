using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Formatting;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// Settings → Downloads (#327, #331): which collections are kept offline, in which format, on metered connections
/// or not; the status of the downloads; Sync now and Remove all. Turning on a collection that downloads more
/// than <see cref="DownloadManager.LargeDownloadBytes"/> asks first; the switch shows on meanwhile. The folder can
/// be changed, which moves the downloads with progress (#399). Create it per visit and dispose it.
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
    private readonly IFolderPicker _picker;

    // The switches turning on while their size is checked, by property name.
    private readonly HashSet<string> _turningOn = [];
    private bool _checkingSize;
    private CancellationTokenSource? _moveCancel;

    public DownloadsViewModel(
        DownloadManager downloads,
        ISettingsService settings,
        INetworkStatus network,
        IDialogService dialogs,
        IStringResources strings,
        IDispatcher dispatcher,
        IFolderPicker picker)
    {
        _downloads = downloads;
        _settings = settings;
        _network = network;
        _dialogs = dialogs;
        _strings = strings;
        _dispatcher = dispatcher;
        _picker = picker;
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
        get => IsOn(nameof(FavouriteSongs), Current.FavouriteSongs);
        set => Switch(nameof(FavouriteSongs), value, (d, on) => d with { FavouriteSongs = on });
    }

    public bool FavouriteAlbums
    {
        get => IsOn(nameof(FavouriteAlbums), Current.FavouriteAlbums);
        set => Switch(nameof(FavouriteAlbums), value, (d, on) => d with { FavouriteAlbums = on });
    }

    public bool FavouriteArtists
    {
        get => IsOn(nameof(FavouriteArtists), Current.FavouriteArtists);
        set => Switch(nameof(FavouriteArtists), value, (d, on) => d with { FavouriteArtists = on });
    }

    public bool Playlists
    {
        get => IsOn(nameof(Playlists), Current.Playlists);
        set => Switch(nameof(Playlists), value, (d, on) => d with { Playlists = on });
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

    /// <summary>Gets the folder that holds the downloads (#399).</summary>
    public string FolderText => _downloads.Folder;

    /// <summary>Gets whether a folder was chosen: Use the default folder then moves them back.</summary>
    public bool IsCustomFolder => Current.Folder is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotMoving))]
    [NotifyCanExecuteChangedFor(nameof(ChangeFolderCommand), nameof(UseDefaultFolderCommand), nameof(SyncNowCommand), nameof(RemoveAllCommand), nameof(CancelMoveCommand))]
    public partial bool IsMoving { get; private set; }

    public bool IsNotMoving => !IsMoving;

    /// <summary>Gets how far the move is, 0 to 100.</summary>
    [ObservableProperty]
    public partial double MoveProgress { get; private set; }

    /// <summary>Gets e.g. "1.2 GB of 4.5 GB moved".</summary>
    [ObservableProperty]
    public partial string MoveText { get; private set; } = string.Empty;

    /// <summary>Gets why the last move did not happen, or an empty string.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoveMessage))]
    public partial string MoveMessage { get; private set; } = string.Empty;

    public bool HasMoveMessage => MoveMessage.Length > 0;

    private DownloadSettings Current => _settings.Current.Downloads;

    /// <summary>Shows what is on disk at once, before the next sync.</summary>
    public Task LoadAsync() => _downloads.RefreshStatusAsync();

    public void Dispose()
    {
        // Leaving the page does not stop a move: it ends on its own, and the next visit shows the folder.
        _downloads.StatusChanged -= OnStatusChanged;
        _settings.Changed -= OnSettingsChanged;
        _network.Changed -= OnNetworkChanged;
    }

    private bool CanSyncNow() => Current.AnyCollection && !IsSyncing && !IsMoving && _network.IsOnline;

    private bool CanRemoveAll() => HasDownloads && !IsMoving;

    private bool CanUseDefaultFolder() => IsCustomFolder && !IsMoving;

    /// <summary>Lets the user choose a folder and moves the downloads there (#399).</summary>
    [RelayCommand(CanExecute = nameof(IsNotMoving))]
    private async Task ChangeFolderAsync()
    {
        if (await _picker.PickFolderAsync() is { } folder)
        {
            await MoveToAsync(folder);
        }
    }

    /// <summary>Moves the downloads back to the app's local data.</summary>
    [RelayCommand(CanExecute = nameof(CanUseDefaultFolder))]
    private Task UseDefaultFolderAsync() => MoveToAsync(null);

    [RelayCommand(CanExecute = nameof(IsMoving))]
    private void CancelMove() => _moveCancel?.Cancel();

    private async Task MoveToAsync(string? folder)
    {
        using var cancel = new CancellationTokenSource();
        _moveCancel = cancel;
        MoveMessage = string.Empty;
        MoveProgress = 0;
        MoveText = _strings.GetString("Downloads_MoveStarting");
        IsMoving = true;
        DownloadMoveResult result;
        try
        {
            result = await _downloads.MoveAsync(folder, new Progress<DownloadMoveProgress>(ShowProgress), cancel.Token);
        }
        finally
        {
            _moveCancel = null;
            IsMoving = false;
        }

        MoveMessage = result.Outcome switch
        {
            DownloadMoveOutcome.NotEnoughSpace => _strings.GetString("Downloads_MoveNotEnoughSpace")
                .Replace("{0}", ByteSizes.Format(result.Needed, _strings), StringComparison.Ordinal)
                .Replace("{1}", ByteSizes.Format(result.Free, _strings), StringComparison.Ordinal),
            DownloadMoveOutcome.NotLocal => _strings.GetString("Downloads_MoveNotLocal"),
            DownloadMoveOutcome.Inside => _strings.GetString("Downloads_MoveInside"),
            DownloadMoveOutcome.Failed => _strings.GetString("Downloads_MoveFailed"),
            _ => string.Empty,
        };
        OnPropertyChanged(nameof(FolderText));
        OnPropertyChanged(nameof(IsCustomFolder));
        UseDefaultFolderCommand.NotifyCanExecuteChanged();
    }

    private void ShowProgress(DownloadMoveProgress progress)
    {
        // A report can come after the end.
        if (!IsMoving)
        {
            return;
        }

        MoveProgress = progress.Total > 0 ? 100.0 * progress.Moved / progress.Total : 0;
        MoveText = _strings.GetString("Downloads_MoveProgress")
            .Replace("{0}", ByteSizes.Format(progress.Moved, _strings), StringComparison.Ordinal)
            .Replace("{1}", ByteSizes.Format(progress.Total, _strings), StringComparison.Ordinal);
    }

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
    [RelayCommand(CanExecute = nameof(CanRemoveAll))]
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

        _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteSongs = false, FavouriteAlbums = false, FavouriteArtists = false, Playlists = false } });
        await _downloads.RemoveAllAsync();
    }

    private bool IsOn(string name, bool saved) => saved || _turningOn.Contains(name);

    /// <summary>Turns a collection off at once, or on after checking its size (see the class summary).</summary>
    private void Switch(string name, bool on, Func<DownloadSettings, bool, DownloadSettings> change)
    {
        var saved = change(Current, true) == Current;
        if (on == IsOn(name, saved))
        {
            return;
        }

        if (!on)
        {
            Change(true, d => change(d, false));
            return;
        }

        _turningOn.Add(name);
        _ = TurnOnAsync(name, change);
    }

    private async Task TurnOnAsync(string name, Func<DownloadSettings, bool, DownloadSettings> change)
    {
        _checkingSize = true;
        Update(_downloads.Status);
        try
        {
            // This collection alone: songs other collections download anyway do not count.
            var alone = change(new DownloadSettings { Format = Current.Format, AacBitrateKbps = Current.AacBitrateKbps }, true);
            DownloadEstimate? estimate = null;
            try
            {
                estimate = await _downloads.EstimateAsync(alone);
            }
            catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
            {
                // Offline or signed out: no question, the downloads wait for the server anyway.
            }

            if (estimate is { Bytes: > DownloadManager.LargeDownloadBytes } large && !await ConfirmLargeAsync(large))
            {
                return;
            }

            Change(true, d => change(d, true));
        }
        finally
        {
            _turningOn.Remove(name);
            _checkingSize = false;
            OnPropertyChanged(name);
            Update(_downloads.Status);
        }
    }

    private Task<bool> ConfirmLargeAsync(DownloadEstimate estimate) =>
        _dialogs.ConfirmAsync(
            _strings.GetString("Downloads_LargeTitle").Replace("{0}", estimate.Songs.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal),
            _strings.GetString("Downloads_LargeMessage").Replace("{0}", ByteSizes.Format(estimate.Bytes, _strings), StringComparison.Ordinal),
            _strings.GetString("Downloads_LargeConfirm"),
            _strings.GetString("Dialog_Cancel"));

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
        OnPropertyChanged(nameof(FavouriteAlbums));
        OnPropertyChanged(nameof(FavouriteArtists));
        OnPropertyChanged(nameof(Playlists));
        OnPropertyChanged(nameof(FormatIndex));
        OnPropertyChanged(nameof(AllowMetered));
        OnPropertyChanged(nameof(FolderText));
        OnPropertyChanged(nameof(IsCustomFolder));
        UseDefaultFolderCommand.NotifyCanExecuteChanged();
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
            _ when _checkingSize => _strings.GetString("Downloads_CheckingSize"),
            { Current: { } name } => _strings.GetString("Downloads_Current").Replace("{0}", name, StringComparison.Ordinal),
            { IsSyncing: true } => _strings.GetString("Downloads_Checking"),
            { Stopped: DownloadStop.None } => string.Empty,
            { Stopped: var stop } when !Current.AnyCollection && stop is not (DownloadStop.LowDisk or DownloadStop.FolderMissing) => string.Empty,
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
