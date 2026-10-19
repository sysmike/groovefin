using System.Globalization;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Formatting;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.Menus;

/// <summary>What a context menu entry does; views pick the icon by kind.</summary>
public enum MenuActionKind
{
    PlayNow,
    PlayNext,
    AddToQueue,

    /// <summary>A submenu: "New playlist…" and the user's playlists, for <see cref="MenuAction.Tracks"/>.</summary>
    AddToPlaylist,
    GoToAlbum,
    GoToArtist,
    InstantMix,
    Favourite,
    Unfavourite,
    ShowInfo,
}

/// <summary>
/// One context menu entry. <see cref="InvokeAsync"/> runs it; failures are shown by the action itself.
/// For <see cref="MenuActionKind.AddToPlaylist"/>, <see cref="Tracks"/> loads the tracks to add.
/// </summary>
public sealed record MenuAction(MenuActionKind Kind, string Text, Func<Task> InvokeAsync)
{
    public Func<Task<IReadOnlyList<Track>>>? Tracks { get; init; }
}

/// <summary>
/// The context menus of tracks, albums, artists and playlists, the same on every page: play now, play
/// next, add to queue, add to playlist, go to album or artist, instant mix, favourite and show info.
/// Album, artist, playlist and genre actions load their tracks when they run.
/// </summary>
public sealed class ContextMenus(
    IPlayer player,
    ILibraryService library,
    IPlaylistService playlists,
    INavigationService navigation,
    FavouritesService favourites,
    InstantMix instantMix,
    IDialogService dialogs,
    IErrorReporter errors,
    IStringResources strings)
{
    /// <summary>Most tracks a genre entry plays or queues.</summary>
    public const int GenreTrackLimit = 1000;

    /// <summary>Most tracks a folder entry plays or queues, subfolders included.</summary>
    public const int FolderTrackLimit = 1000;

    public IReadOnlyList<MenuAction> ForTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        Task<IReadOnlyList<Track>> Tracks() => Task.FromResult<IReadOnlyList<Track>>([track]);

        var actions = new List<MenuAction>(Playback(Tracks)) { AddTo(Tracks) };
        if (track.AlbumId is { } albumId)
        {
            actions.Add(Action(MenuActionKind.GoToAlbum, "Menu_GoToAlbum", () => navigation.NavigateTo(PageKeys.Album, albumId)));
        }

        actions.AddRange(GoToArtists(track.Artists));
        actions.Add(Mix(track.Id));
        actions.Add(FavouriteToggle(track.Id, track.IsFavorite));
        actions.Add(new MenuAction(MenuActionKind.ShowInfo, strings.GetString("Menu_ShowInfo"), () => ShowTrackInfoAsync(track)));
        return actions;
    }

    /// <summary>The menu of several selected tracks: playback and add to playlist; one track gets <see cref="ForTrack"/>.</summary>
    public IReadOnlyList<MenuAction> ForTracks(IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 1)
        {
            return ForTrack(tracks[0]);
        }

        Task<IReadOnlyList<Track>> Tracks() => Task.FromResult(tracks);
        return [.. Playback(Tracks), AddTo(Tracks)];
    }

    public IReadOnlyList<MenuAction> ForAlbum(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        Task<IReadOnlyList<Track>> Tracks() => library.GetAlbumTracksAsync(album.Id);

        var actions = new List<MenuAction>(Playback(Tracks)) { AddTo(Tracks) };
        actions.AddRange(GoToArtists(album.AlbumArtists));
        actions.Add(Mix(album.Id));
        actions.Add(FavouriteToggle(album.Id, album.IsFavorite));
        return actions;
    }

    public IReadOnlyList<MenuAction> ForArtist(Artist artist)
    {
        ArgumentNullException.ThrowIfNull(artist);
        Task<IReadOnlyList<Track>> Tracks() => library.GetArtistTracksAsync(artist.Id);

        var actions = new List<MenuAction>(Playback(Tracks)) { AddTo(Tracks), Mix(artist.Id), FavouriteToggle(artist.Id, artist.IsFavorite) };
        return actions;
    }

    public IReadOnlyList<MenuAction> ForPlaylist(Playlist playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        async Task<IReadOnlyList<Track>> Tracks() => [.. (await playlists.GetEntriesAsync(playlist.Id)).Select(e => e.Track)];

        return [.. Playback(Tracks), Mix(playlist.Id)];
    }

    public IReadOnlyList<MenuAction> ForGenre(Genre genre)
    {
        ArgumentNullException.ThrowIfNull(genre);
        Task<IReadOnlyList<Track>> Tracks() => library.GetGenreTracksAsync(genre.Id, GenreTrackLimit);

        return [.. Playback(Tracks), Mix(genre.Id)];
    }

    /// <summary>A folder with its subfolders: playback and add to playlist.</summary>
    public IReadOnlyList<MenuAction> ForFolder(Folder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        Task<IReadOnlyList<Track>> Tracks() => library.GetFolderTracksAsync(folder.Id, FolderTrackLimit);

        return [.. Playback(Tracks), AddTo(Tracks)];
    }

    private IEnumerable<MenuAction> Playback(Func<Task<IReadOnlyList<Track>>> tracks)
    {
        yield return new MenuAction(MenuActionKind.PlayNow, strings.GetString("Menu_PlayNow"), () => WithTracksAsync(tracks, t => player.Play(t)));
        yield return new MenuAction(MenuActionKind.PlayNext, strings.GetString("Menu_PlayNext"), () => WithTracksAsync(tracks, player.PlayNext));
        yield return new MenuAction(MenuActionKind.AddToQueue, strings.GetString("Menu_AddToQueue"), () => WithTracksAsync(tracks, player.AddToQueue));
    }

    private MenuAction AddTo(Func<Task<IReadOnlyList<Track>>> tracks) =>
        new(MenuActionKind.AddToPlaylist, strings.GetString("Menu_AddToPlaylist"), () => Task.CompletedTask) { Tracks = tracks };

    private IEnumerable<MenuAction> GoToArtists(IReadOnlyList<ArtistRef> artists) =>
        artists.Select(artist => Action(
            MenuActionKind.GoToArtist,
            strings.GetString("Menu_GoToArtist").Replace("{0}", artist.Name, StringComparison.Ordinal),
            () => navigation.NavigateTo(PageKeys.Artist, artist.Id)));

    private MenuAction Mix(string itemId) =>
        new(MenuActionKind.InstantMix, strings.GetString("Menu_InstantMix"), () => instantMix.StartAsync(itemId));

    private MenuAction FavouriteToggle(string itemId, bool loaded)
    {
        var isFavourite = favourites.IsFavourite(itemId, loaded);
        return new MenuAction(
            isFavourite ? MenuActionKind.Unfavourite : MenuActionKind.Favourite,
            strings.GetString(isFavourite ? "Menu_Unfavourite" : "Menu_Favourite"),
            () => favourites.SetAsync(itemId, !isFavourite));
    }

    private static MenuAction Action(MenuActionKind kind, string text, Action invoke) =>
        new(kind, text, () =>
        {
            invoke();
            return Task.CompletedTask;
        });

    /// <summary>Loads the tracks and hands them on; loading failures are shown, an empty list does nothing.</summary>
    private async Task WithTracksAsync(Func<Task<IReadOnlyList<Track>>> load, Action<IReadOnlyList<Track>> use)
    {
        try
        {
            var tracks = await load();
            if (tracks.Count > 0)
            {
                use(tracks);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.Playback);
        }
    }

    private Task ShowTrackInfoAsync(Track track)
    {
        var details = new List<KeyValuePair<string, string>>();
        void Add(string labelKey, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                details.Add(new(strings.GetString(labelKey), value));
            }
        }

        Add("Info_Title", track.Name);
        Add("Info_Artists", string.Join(", ", track.Artists.Select(a => a.Name)));
        Add("Info_Album", track.AlbumName);
        Add("Info_Track", track.TrackNumber?.ToString(CultureInfo.CurrentCulture));
        Add("Info_Disc", track.DiscNumber?.ToString(CultureInfo.CurrentCulture));
        Add("Info_Length", Durations.Track(track.Duration));
        Add("Info_PlayCount", track.PlayCount.ToString(CultureInfo.CurrentCulture));
        return dialogs.ShowDetailsAsync(track.Name, details, strings.GetString("Dialog_Close"));
    }
}
