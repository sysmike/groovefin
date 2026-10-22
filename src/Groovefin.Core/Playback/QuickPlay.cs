using Groovefin.Core.Api;
using Groovefin.Core.Menus;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;

namespace Groovefin.Core.Playback;

/// <summary>
/// Plays an album, an artist, a playlist or a genre straight from its tile (the play button on the cover,
/// #185, #191), the same tracks as "Play now" in its context menu. Loading failures are shown; an empty
/// list does nothing.
/// </summary>
public sealed class QuickPlay(ILibraryService library, IPlaylistService playlists, IPlayer player, IErrorReporter errors)
{
    public Task PlayAlbumAsync(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        return PlayAsync(() => library.GetAlbumTracksAsync(album.Id));
    }

    public Task PlayArtistAsync(Artist artist)
    {
        ArgumentNullException.ThrowIfNull(artist);
        return PlayAsync(() => library.GetArtistTracksAsync(artist.Id));
    }

    public Task PlayPlaylistAsync(Playlist playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        return PlayAsync(async () => [.. (await playlists.GetEntriesAsync(playlist.Id)).Select(e => e.Track)]);
    }

    /// <summary>Plays the first <see cref="ContextMenus.GenreTrackLimit"/> tracks of a genre.</summary>
    public Task PlayGenreAsync(Genre genre)
    {
        ArgumentNullException.ThrowIfNull(genre);
        return PlayAsync(() => library.GetGenreTracksAsync(genre.Id, ContextMenus.GenreTrackLimit));
    }

    private async Task PlayAsync(Func<Task<IReadOnlyList<Track>>> load)
    {
        try
        {
            var tracks = await load();
            if (tracks.Count > 0)
            {
                player.Play(tracks);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.Playback);
        }
    }
}
