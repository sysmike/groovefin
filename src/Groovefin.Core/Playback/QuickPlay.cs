using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;

namespace Groovefin.Core.Playback;

/// <summary>
/// Plays an album or an artist straight from its tile (the play button on the cover, #185), the same
/// tracks as "Play now" in its context menu. Loading failures are shown; an empty list does nothing.
/// </summary>
public sealed class QuickPlay(ILibraryService library, IPlayer player, IErrorReporter errors)
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
