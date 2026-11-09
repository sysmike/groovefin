using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Downloads;
using Groovefin.Core.Formatting;
using Groovefin.Core.Navigation;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>One download on the Downloads page (#388): what it is, how far it is, Retry and Remove.</summary>
public sealed partial class DownloadItemRow : ObservableObject
{
    private readonly IStringResources _strings;
    private readonly Action<DownloadItemRow> _retry;
    private readonly Func<DownloadItemRow, Task> _remove;

    internal DownloadItemRow(DownloadItem item, IStringResources strings, Action<DownloadItemRow> retry, Func<DownloadItemRow, Task> remove)
    {
        _strings = strings;
        _retry = retry;
        _remove = remove;
        Kind = item.Kind;
        Id = item.Id;
        Name = item.Kind == DownloadItemKind.FavouriteSongs ? strings.GetString("Downloads_FavouriteSongs") : item.Name;
        Glyph = item.Kind switch
        {
            DownloadItemKind.FavouriteSongs => "",
            DownloadItemKind.Playlist => "",
            DownloadItemKind.Album => "",
            _ => "",
        };
        RetryName = strings.GetString("Downloads_RetryName").Replace("{0}", Name, StringComparison.Ordinal);
        RemoveName = strings.GetString("Downloads_RemoveName").Replace("{0}", Name, StringComparison.Ordinal);
        Item = item;
        Update(item);
    }

    public DownloadItemKind Kind { get; }

    public string Id { get; }

    public string Name { get; }

    /// <summary>Gets the kind's icon, a Segoe Fluent Icons glyph.</summary>
    public string Glyph { get; }

    /// <summary>Gets the Retry button's name for Narrator, e.g. "Retry Road trip".</summary>
    public string RetryName { get; }

    /// <summary>Gets the Remove button's name for Narrator, e.g. "Remove Road trip from the downloads".</summary>
    public string RemoveName { get; }

    /// <summary>Gets the download as last read.</summary>
    public DownloadItem Item { get; private set; }

    /// <summary>Gets e.g. "Album by Daft Punk".</summary>
    [ObservableProperty]
    public partial string Detail { get; private set; } = string.Empty;

    /// <summary>Gets e.g. "14 songs · 540 MB", or why songs failed.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CanRetry { get; private set; }

    /// <summary>Gets the row's text for Narrator: its name, kind and status.</summary>
    public override string ToString() => $"{Name}, {Detail}, {StatusText}";

    internal void Update(DownloadItem item)
    {
        Item = item;
        Detail = item.Kind switch
        {
            DownloadItemKind.FavouriteSongs => _strings.GetString("Downloads_KindFavouriteSongs"),
            DownloadItemKind.Playlist => _strings.GetString("Downloads_KindPlaylist"),
            DownloadItemKind.Album when !string.IsNullOrEmpty(item.Artist) => _strings.GetString("Downloads_KindAlbumBy").Replace("{0}", item.Artist, StringComparison.Ordinal),
            DownloadItemKind.Album => _strings.GetString("Downloads_KindAlbum"),
            _ => _strings.GetString("Downloads_KindArtist"),
        };
        var songs = item.TrackIds.Distinct(StringComparer.Ordinal).Count().ToString("N0", CultureInfo.CurrentCulture);
        var downloaded = item.Downloaded.ToString("N0", CultureInfo.CurrentCulture);
        var size = ByteSizes.Format(item.Bytes, _strings);
        StatusText = item.State switch
        {
            DownloadItemState.Downloaded => _strings.GetString("Downloads_ItemDownloaded")
                .Replace("{0}", songs, StringComparison.Ordinal)
                .Replace("{1}", size, StringComparison.Ordinal),
            DownloadItemState.Failed => _strings.GetString("Downloads_ItemFailed")
                .Replace("{0}", item.Failures.Count.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{1}", item.Failures[0].TrackName, StringComparison.Ordinal)
                .Replace("{2}", item.Failures[0].Reason, StringComparison.Ordinal),
            _ => _strings.GetString(item.State == DownloadItemState.Downloading ? "Downloads_ItemDownloading" : "Downloads_ItemWaiting")
                .Replace("{0}", downloaded, StringComparison.Ordinal)
                .Replace("{1}", songs, StringComparison.Ordinal)
                .Replace("{2}", size, StringComparison.Ordinal),
        };
        CanRetry = item.State == DownloadItemState.Failed;
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry() => _retry(this);

    [RelayCommand]
    private Task RemoveAsync() => _remove(this);
}

/// <summary>A playlist, album or artist removed from the downloads (#388), with Download again.</summary>
public sealed partial class RemovedDownloadRow : ObservableObject
{
    private readonly Action<RemovedDownloadRow> _restore;

    internal RemovedDownloadRow(DownloadExclusion removed, IStringResources strings, Action<RemovedDownloadRow> restore)
    {
        _restore = restore;
        Removed = removed;
        Detail = strings.GetString(removed.Kind switch
        {
            DownloadItemKind.Playlist => "Downloads_KindPlaylist",
            DownloadItemKind.Album => "Downloads_KindAlbum",
            _ => "Downloads_KindArtist",
        });
        RestoreName = strings.GetString("Downloads_RestoreName").Replace("{0}", removed.Name, StringComparison.Ordinal);
    }

    public DownloadExclusion Removed { get; }

    public string Name => Removed.Name;

    public string Detail { get; }

    /// <summary>Gets the button's name for Narrator, e.g. "Download Road trip again".</summary>
    public string RestoreName { get; }

    public override string ToString() => $"{Name}, {Detail}";

    [RelayCommand]
    private void Restore() => _restore(this);
}

/// <summary>
/// The Downloads page (#388): every download with its size and status, Retry for the failed and Remove for each,
/// what was removed with Download again, and the space used and free. Opened from Settings → Downloads; follows
/// the downloads while it shows. Create it per visit and dispose it.
/// </summary>
public sealed partial class DownloadListViewModel : ObservableObject, IDisposable
{
    private readonly DownloadManager _downloads;
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IStringResources _strings;
    private readonly IDispatcher _dispatcher;
    private bool _loaded;

    public DownloadListViewModel(
        DownloadManager downloads,
        ISettingsService settings,
        INavigationService navigation,
        IDialogService dialogs,
        IStringResources strings,
        IDispatcher dispatcher)
    {
        _downloads = downloads;
        _settings = settings;
        _navigation = navigation;
        _dialogs = dialogs;
        _strings = strings;
        _dispatcher = dispatcher;
        _downloads.StatusChanged += OnStatusChanged;
        _settings.Changed += OnSettingsChanged;
    }

    public ObservableCollection<DownloadItemRow> Items { get; } = [];

    public ObservableCollection<RemovedDownloadRow> Removed { get; } = [];

    /// <summary>Gets e.g. "9.8 GB used · 120 GB free on the drive".</summary>
    [ObservableProperty]
    public partial string SpaceText { get; private set; } = string.Empty;

    /// <summary>Gets what happens now, or why nothing does, as in Settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivity))]
    public partial string ActivityText { get; private set; } = string.Empty;

    public bool HasActivity => ActivityText.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasItems { get; private set; }

    [ObservableProperty]
    public partial bool HasRemoved { get; private set; }

    /// <summary>Gets a value indicating whether the downloads were read and there are none.</summary>
    public bool IsEmpty => _loaded && !HasItems;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    public partial bool IsSyncing { get; private set; }

    /// <summary>Reads what is on disk, then shows it.</summary>
    public async Task LoadAsync()
    {
        await _downloads.RefreshStatusAsync();
        _loaded = true;
        Update();
    }

    public void Dispose()
    {
        _downloads.StatusChanged -= OnStatusChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    private bool CanSyncNow() => !IsSyncing && _settings.Current.Downloads.AnyCollection;

    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    private Task SyncNowAsync()
    {
        IsSyncing = true;
        return _downloads.SyncNowAsync();
    }

    [RelayCommand]
    private void OpenSettings() => _navigation.NavigateTo(PageKeys.Settings);

    private void Retry(DownloadItemRow row) => _ = _downloads.RetryAsync(row.Item);

    /// <summary>Removes one download after a confirmation; the others stay as they are.</summary>
    private async Task RemoveAsync(DownloadItemRow row)
    {
        var message = row.Kind == DownloadItemKind.FavouriteSongs ? "Downloads_RemoveFavouritesMessage" : "Downloads_RemoveItemMessage";
        if (!await _dialogs.ConfirmAsync(
                _strings.GetString("Downloads_RemoveItemTitle").Replace("{0}", row.Name, StringComparison.Ordinal),
                _strings.GetString(message),
                _strings.GetString("Downloads_RemoveItemConfirm"),
                _strings.GetString("Dialog_Cancel")))
        {
            return;
        }

        _downloads.Remove(row.Item);
    }

    private void Restore(RemovedDownloadRow row) => _downloads.Restore(row.Removed);

    private void OnStatusChanged(object? sender, EventArgs e) => _ = _dispatcher.EnqueueAsync(Update);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Downloads != e.NewSettings.Downloads)
        {
            _ = _dispatcher.EnqueueAsync(Update);
        }
    }

    /// <summary>Updates the rows in place, so the focus stays on a button while songs download.</summary>
    private void Update()
    {
        if (!_loaded)
        {
            return;
        }

        var status = _downloads.Status;
        IsSyncing = status.IsSyncing;
        SyncNowCommand.NotifyCanExecuteChanged();
        ActivityText = DownloadsViewModel.Activity(status, _settings.Current.Downloads.AnyCollection, _strings);
        var used = ByteSizes.Format(status.Bytes, _strings);
        SpaceText = _downloads.FreeBytes() is { } free
            ? _strings.GetString("Downloads_Space").Replace("{0}", used, StringComparison.Ordinal).Replace("{1}", ByteSizes.Format(free, _strings), StringComparison.Ordinal)
            : _strings.GetString("Downloads_SpaceUsed").Replace("{0}", used, StringComparison.Ordinal);

        var items = _downloads.GetItems();
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!items.Any(item => Same(item, Items[i])))
            {
                Items.RemoveAt(i);
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            var existing = Items.FirstOrDefault(row => Same(items[i], row));
            if (existing is null)
            {
                Items.Insert(Math.Min(i, Items.Count), new DownloadItemRow(items[i], _strings, Retry, RemoveAsync));
                continue;
            }

            existing.Update(items[i]);
            var at = Items.IndexOf(existing);
            if (at != i && i < Items.Count)
            {
                Items.Move(at, i);
            }
        }

        HasItems = Items.Count > 0;
        OnPropertyChanged(nameof(IsEmpty));

        var removed = _downloads.Removed;
        if (!removed.SequenceEqual(Removed.Select(r => r.Removed)))
        {
            Removed.Clear();
            foreach (var exclusion in removed)
            {
                Removed.Add(new RemovedDownloadRow(exclusion, _strings, Restore));
            }
        }

        HasRemoved = Removed.Count > 0;
    }

    private static bool Same(DownloadItem item, DownloadItemRow row) => item.Kind == row.Kind && item.Id == row.Id;
}
