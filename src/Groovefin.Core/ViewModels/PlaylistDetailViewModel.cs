using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Formatting;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Search;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// A row of a playlist, in the album page's columns: position, title (after the album's cover, #262),
/// artists, album, heart, length (#245).
/// </summary>
public sealed partial class PlaylistEntryItem(PlaylistEntry entry, bool isFavourite = false, ImageRequest? cover = null) : ObservableObject, ICurrentTrackRow
{
    public PlaylistEntry Entry { get; } = entry;

    /// <summary>Gets the cover of the track's album, or <see langword="null"/> for the placeholder.</summary>
    public ImageRequest? Cover { get; } = cover;

    Track ICurrentTrackRow.Track => Entry.Track;

    /// <summary>Gets or sets whether the row's track is playing (#247).</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>Gets or sets a value indicating whether the track is downloaded (#337).</summary>
    [ObservableProperty]
    public partial bool IsDownloaded { get; set; }

    /// <summary>Gets or sets the 1-based position in the playlist; the view model renumbers after each edit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Number), nameof(AccessibleName))]
    public partial int Position { get; set; }

    /// <summary>Gets or sets the row's heart; kept in line with the server by the view model.</summary>
    [ObservableProperty]
    public partial bool IsFavourite { get; set; } = isFavourite;

    public string Number => Position > 0 ? Position.ToString(CultureInfo.CurrentCulture) : string.Empty;

    public string Name => Entry.Track.Name;

    public string Artist => string.Join(", ", Entry.Track.Artists.Select(a => a.Name));

    public string Album => Entry.Track.AlbumName ?? string.Empty;

    public string Duration => Durations.Track(Entry.Track.Duration);

    /// <summary>Gets the text Narrator reads, e.g. "3, Digital Love, Daft Punk, Discovery, 4:58".</summary>
    public string AccessibleName => string.Join(", ", new[] { Number, Name, Artist, Album, Duration }.Where(s => s.Length > 0));
}

/// <summary>
/// Playlist detail page: play, shuffle, queue, rename, delete (after a confirmation), and edit the
/// entries (remove after a confirmation, move up/down, drag), and toggle a track's heart or the playlist's, which puts it into the
/// navigation pane (#297). Edits show at once and are sent to the
/// server; when the server refuses, the entries are loaded again and the error is shown. The playlist can
/// be shown in reverse order, last entry first (#267): only here, remembered per playlist, and played as
/// shown; the server's order stays, so moving entries is off meanwhile.
/// </summary>
public sealed partial class PlaylistDetailViewModel(
    IPlaylistService playlists,
    IPlayer player,
    INavigationService navigation,
    IDialogService dialogs,
    IErrorReporter errors,
    IStringResources strings,
    FavouritesService favourites,
    IMessenger messenger,
    CurrentTrack currentTrack,
    ISettingsService settings) : ObservableObject
{
    public const double ImageWidth = 200;

    /// <summary>The width of a row's cover (the list thumbnail size; a compact list draws it smaller).</summary>
    public const double CoverWidth = 40;

    private bool _loadingOrder;

    /// <summary>Whether the filter follows changes to <see cref="Entries"/> yet (#391).</summary>
    private bool _followsEntries;

    public ObservableCollection<PlaylistEntryItem> Entries { get; } = currentTrack.Watch(new ObservableCollection<PlaylistEntryItem>());

    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaylist), nameof(Name), nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(DeleteCommand), nameof(ToggleReverseCommand), nameof(ToggleFavouriteCommand))]
    public partial Playlist? Playlist { get; private set; }

    /// <summary>Gets a value indicating whether the playlist is a favourite: it is then in the navigation pane (#297).</summary>
    [ObservableProperty]
    public partial bool IsFavourite { get; private set; }

    public bool HasPlaylist => Playlist is not null;

    public string Name => Playlist?.Name ?? string.Empty;

    [ObservableProperty]
    public partial ImageRequest? Image { get; private set; }

    /// <summary>Gets "12 tracks · 1:02:15" for the entries shown.</summary>
    [ObservableProperty]
    public partial string Details { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(ShuffleCommand), nameof(AddToQueueCommand))]
    public partial bool HasEntries { get; private set; }

    /// <summary>Gets a value indicating whether the loaded playlist has no entries.</summary>
    public bool IsEmpty => HasPlaylist && !HasEntries;

    /// <summary>
    /// Gets or sets a value indicating whether the entries are shown last first (#267); a change reverses the
    /// rows and is remembered for the playlist.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReorder))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand))]
    public partial bool IsReversed { get; set; }

    /// <summary>Gets a value indicating whether entries can be moved: only in the playlist's own order, unfiltered.</summary>
    public bool CanReorder => !IsReversed && !IsFiltering;

    /// <summary>
    /// Gets or sets the text the entries are filtered by (#391): every word must start a word of the title, an
    /// artist or the album, or come close to it. Empty shows every entry.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltering), nameof(CanReorder), nameof(ShownEntries))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand))]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether a filter is typed; the list then shows <see cref="FilteredEntries"/> and cannot be reordered.</summary>
    public bool IsFiltering => !string.IsNullOrWhiteSpace(FilterText);

    /// <summary>Gets the entries that match <see cref="FilterText"/>, in the list's order.</summary>
    public ObservableCollection<PlaylistEntryItem> FilteredEntries { get; } = [];

    /// <summary>Gets what the list shows: every entry, or while filtering the matching ones.</summary>
    public IList<PlaylistEntryItem> ShownEntries => IsFiltering ? FilteredEntries : Entries;

    public async Task LoadAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        // Weak: the page's view model needs no unregistering.
        if (!messenger.IsRegistered<FavouriteChangedMessage>(this))
        {
            messenger.Register<PlaylistDetailViewModel, FavouriteChangedMessage>(this, static (r, m) => r.OnFavouriteChanged(m));
        }

        IsLoading = true;
        LoadFailed = false;
        try
        {
            var playlistTask = playlists.GetPlaylistAsync(playlistId, cancellationToken);
            var entriesTask = playlists.GetEntriesAsync(playlistId, cancellationToken);
            var playlist = await playlistTask;
            var entries = await entriesTask;

            _loadingOrder = true;
            IsReversed = settings.Current.Browse.ReversedPlaylists.Contains(playlist.Id);
            _loadingOrder = false;
            IsFavourite = favourites.IsFavourite(playlist.Id, playlist.IsFavorite);
            Playlist = playlist with { IsFavorite = IsFavourite };
            Image = ImageRequest.Primary(playlist.Id, playlist.PrimaryImageTag, ImageWidth, DisplayScale);
            ShowEntries(entries);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadPlaylist);
        }
        catch (OperationCanceledException)
        {
            // The page was closed.
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Sends a move the list already made (a drag): <paramref name="item"/> is now at
    /// <paramref name="newIndex"/> in <see cref="Entries"/>.
    /// </summary>
    public Task MoveEntryAsync(PlaylistEntryItem item, int newIndex)
    {
        if (IsReversed)
        {
            return Task.CompletedTask;
        }

        Renumber();
        return EditAsync(() => playlists.MoveEntryAsync(Playlist!.Id, item.Entry.EntryId, newIndex));
    }

    private IReadOnlyList<Track> Tracks => [.. Entries.Select(e => e.Entry.Track)];

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void Play() => player.Play(Tracks);

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void Shuffle() => player.Shuffle(Tracks);

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void AddToQueue() => player.AddToQueue(Tracks);

    /// <summary>Plays the playlist from <paramref name="item"/> on (a row was clicked).</summary>
    [RelayCommand]
    private void PlayEntry(PlaylistEntryItem item) => player.Play(Tracks, Math.Max(0, Entries.IndexOf(item)));

    /// <summary>Shows the entries the other way round (#267).</summary>
    [RelayCommand(CanExecute = nameof(HasPlaylist))]
    private void ToggleReverse() => IsReversed = !IsReversed;

    partial void OnIsReversedChanged(bool value)
    {
        if (_loadingOrder || Playlist is not { } playlist)
        {
            return;
        }

        var rows = Entries.Reverse().ToList();
        Entries.Clear();
        foreach (var row in rows)
        {
            Entries.Add(row);
        }

        Renumber();
        RememberReversed(playlist.Id, value);
    }

    private void RememberReversed(string playlistId, bool reversed) =>
        settings.Update(s =>
        {
            var others = s.Browse.ReversedPlaylists.Where(id => id != playlistId);
            return s with { Browse = s.Browse with { ReversedPlaylists = reversed ? [.. others, playlistId] : [.. others] } };
        });

    [RelayCommand(CanExecute = nameof(HasPlaylist))]
    private async Task RenameAsync()
    {
        var playlist = Playlist!;
        var name = await dialogs.PromptAsync(
            strings.GetString("Playlist_RenameTitle"),
            strings.GetString("Playlist_NameLabel"),
            playlist.Name,
            strings.GetString("Dialog_Save"),
            strings.GetString("Dialog_Cancel"));
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == playlist.Name)
        {
            return;
        }

        try
        {
            await playlists.RenameAsync(playlist.Id, name);
            Playlist = playlist with { Name = name.Trim() };
            messenger.Send(new PlaylistChangedMessage(Playlist));
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            errors.Report(ex, Operations.EditPlaylist);
        }
    }

    /// <summary>Deletes the playlist after a confirmation and goes back.</summary>
    [RelayCommand(CanExecute = nameof(HasPlaylist))]
    private async Task DeleteAsync()
    {
        var playlist = Playlist!;
        var confirmed = await dialogs.ConfirmAsync(
            strings.GetString("Playlist_DeleteTitle"),
            strings.GetString("Playlist_DeleteMessage").Replace("{0}", playlist.Name, StringComparison.Ordinal),
            strings.GetString("Playlist_DeleteConfirm"),
            strings.GetString("Dialog_Cancel"));
        if (!confirmed)
        {
            return;
        }

        try
        {
            await playlists.DeleteAsync(playlist.Id);
            messenger.Send(new PlaylistDeletedMessage(playlist.Id));
            RememberReversed(playlist.Id, false);
            Playlist = null;
            navigation.GoBack();
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            errors.Report(ex, Operations.EditPlaylist);
        }
    }

    [RelayCommand]
    private Task RemoveAsync(PlaylistEntryItem item) => RemoveEntriesAsync([item]);

    /// <summary>
    /// Takes the selected entries out at once, after a confirmation (#319), and sends one request for all
    /// (#295); rows already gone are skipped.
    /// </summary>
    public async Task RemoveEntriesAsync(IReadOnlyList<PlaylistEntryItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        List<PlaylistEntryItem> removing = [.. items.Where(Entries.Contains).Distinct()];
        if (removing.Count == 0 || !await ConfirmRemoveAsync(removing))
        {
            return;
        }

        // Rows may have gone while the question was open.
        removing.RemoveAll(item => !Entries.Contains(item));
        if (removing.Count == 0)
        {
            return;
        }

        foreach (var item in removing)
        {
            Entries.Remove(item);
        }

        UpdateDetails();
        List<string> ids = [.. removing.Select(item => item.Entry.EntryId)];
        await EditAsync(() => playlists.RemoveEntriesAsync(Playlist!.Id, ids));
    }

    private Task<bool> ConfirmRemoveAsync(IReadOnlyList<PlaylistEntryItem> items)
    {
        // The playlist goes in first: a song's name may hold "{1}" itself.
        var (key, what) = items.Count == 1
            ? ("Playlist_RemoveOne", items[0].Name)
            : ("Playlist_RemoveSome", items.Count.ToString(CultureInfo.CurrentCulture));
        return dialogs.ConfirmAsync(
            strings.GetString("Playlist_RemoveTitle"),
            strings.GetString(key).Replace("{1}", Name, StringComparison.Ordinal).Replace("{0}", what, StringComparison.Ordinal),
            strings.GetString("Playlist_RemoveConfirm"),
            strings.GetString("Dialog_Cancel"));
    }

    /// <summary>The playlist's heart (#297), optimistic like the album page's; a favourite goes into the navigation pane.</summary>
    [RelayCommand(CanExecute = nameof(HasPlaylist))]
    private async Task ToggleFavouriteAsync()
    {
        var playlist = Playlist!;
        var target = !IsFavourite;
        IsFavourite = target;
        if (await favourites.SetAsync(playlist.Id, target))
        {
            Playlist = playlist with { IsFavorite = target };
            messenger.Send(new PlaylistChangedMessage(Playlist));
        }
        else
        {
            IsFavourite = !target;
        }
    }

    /// <summary>Toggles a track's heart, optimistic like the album page's.</summary>
    [RelayCommand]
    private async Task ToggleEntryFavouriteAsync(PlaylistEntryItem item)
    {
        var target = !item.IsFavourite;
        item.IsFavourite = target;
        if (!await favourites.SetAsync(item.Entry.Track.Id, target))
        {
            item.IsFavourite = !target;
        }
    }

    /// <summary>A heart was toggled elsewhere (now playing bar, favourites page, a context menu).</summary>
    private void OnFavouriteChanged(FavouriteChangedMessage message)
    {
        foreach (var item in Entries.Where(e => e.Entry.Track.Id == message.ItemId))
        {
            item.IsFavourite = message.IsFavourite;
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        if (!_followsEntries)
        {
            // Removing, reversing or reloading entries while a filter is typed shows the change at once.
            _followsEntries = true;
            Entries.CollectionChanged += (_, _) =>
            {
                if (IsFiltering)
                {
                    RefreshFilter();
                }
            };
        }

        RefreshFilter();
    }

    private void RefreshFilter()
    {
        FilteredEntries.Clear();
        if (!IsFiltering)
        {
            return;
        }

        var words = ForgivingSearch.Words(FilterText);
        foreach (var item in Entries)
        {
            var track = item.Entry.Track;
            if (ForgivingSearch.Score(words, [track.Name, track.AlbumName, .. track.Artists.Select(a => a.Name)]) >= 0)
            {
                FilteredEntries.Add(item);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanReorder))]
    private Task MoveUpAsync(PlaylistEntryItem item) => MoveByAsync(item, -1);

    [RelayCommand(CanExecute = nameof(CanReorder))]
    private Task MoveDownAsync(PlaylistEntryItem item) => MoveByAsync(item, 1);

    private Task MoveByAsync(PlaylistEntryItem item, int offset)
    {
        var index = Entries.IndexOf(item);
        var target = index + offset;
        if (!CanReorder || index < 0 || target < 0 || target >= Entries.Count)
        {
            return Task.CompletedTask;
        }

        Entries.Move(index, target);
        return MoveEntryAsync(item, target);
    }

    /// <summary>Sends an edit the list already shows; on failure reloads the entries and reports.</summary>
    private async Task EditAsync(Func<Task> edit)
    {
        if (Playlist is not { } playlist)
        {
            return;
        }

        try
        {
            await edit();
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.EditPlaylist);
            try
            {
                ShowEntries(await playlists.GetEntriesAsync(playlist.Id));
            }
            catch (Exception reload) when (reload is JellyfinException or InvalidOperationException)
            {
                // The edit's error is already shown; the list stays as the user left it.
            }
        }
    }

    private void ShowEntries(IReadOnlyList<PlaylistEntry> entries)
    {
        Entries.Clear();
        foreach (var entry in IsReversed ? entries.Reverse() : entries)
        {
            var track = entry.Track;
            var cover = track.AlbumId is { } albumId ? ImageRequest.Primary(albumId, track.AlbumImageTag, CoverWidth, DisplayScale) : null;
            Entries.Add(new PlaylistEntryItem(entry, favourites.IsFavourite(track.Id, track.IsFavorite), cover));
        }

        UpdateDetails();
    }

    /// <summary>The # column is the position in the playlist's own order, also while shown reversed.</summary>
    private void Renumber()
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            Entries[i].Position = IsReversed ? Entries.Count - i : i + 1;
        }
    }

    private void UpdateDetails()
    {
        Renumber();
        HasEntries = Entries.Count > 0;
        var known = Entries.Select(e => e.Entry.Track.Duration).Where(d => d is not null).Select(d => d!.Value).ToList();
        TimeSpan? total = known.Count > 0 ? known.Aggregate(TimeSpan.Zero, (sum, d) => sum + d) : null;
        Details = PlaylistItem.DetailsText(Entries.Count, total, strings);

        // The navigation pane shows a favourite's count and length too (#299).
        if (Playlist is { } playlist && (playlist.TrackCount != Entries.Count || playlist.Duration != total))
        {
            Playlist = playlist with { TrackCount = Entries.Count, Duration = total };
            messenger.Send(new PlaylistChangedMessage(Playlist));
        }
    }
}
