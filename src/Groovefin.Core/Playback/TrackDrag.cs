using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;

namespace Groovefin.Core.Playback;

/// <summary>
/// Drag and drop of tracks and albums within the app. A drag source calls <see cref="Begin"/> or
/// <see cref="BeginAlbum"/> and puts the returned token into the drag data under <see cref="DataFormat"/>;
/// a drop target passes the token back to <see cref="DropOnQueueAsync"/> or <see cref="DropOnPlaylistAsync"/>.
/// Album tracks are loaded only when dropped. Only the latest drag is kept.
/// </summary>
public sealed class TrackDrag(ILibraryService library, IPlayer player, AddToPlaylist addToPlaylist, IErrorReporter errors)
{
    /// <summary>The drag data format that holds the token.</summary>
    public const string DataFormat = "Groovefin.Tracks";

    private string? _token;
    private Func<Task<IReadOnlyList<Track>>>? _load;

    /// <summary>Starts dragging <paramref name="tracks"/> and returns the token for the drag data.</summary>
    public string Begin(IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        return Begin(() => Task.FromResult(tracks));
    }

    /// <summary>Starts dragging the tracks of <paramref name="album"/> and returns the token for the drag data.</summary>
    public string BeginAlbum(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        return Begin(() => library.GetAlbumTracksAsync(album.Id));
    }

    /// <summary>Gets a value indicating whether <paramref name="token"/> belongs to the latest drag.</summary>
    public bool IsCurrent(string? token) => token is not null && token == _token;

    /// <summary>Inserts the dragged tracks into the queue at <paramref name="position"/> among the upcoming tracks.</summary>
    public async Task DropOnQueueAsync(string? token, int position)
    {
        var tracks = await LoadAsync(token, Operations.Playback);
        if (tracks.Count > 0)
        {
            player.InsertIntoQueue(tracks, Math.Max(0, position));
        }
    }

    /// <summary>Appends the dragged tracks to <paramref name="playlist"/>; the result is shown as a notification.</summary>
    public async Task DropOnPlaylistAsync(string? token, Playlist playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        var tracks = await LoadAsync(token, Operations.EditPlaylist);
        if (tracks.Count > 0)
        {
            await addToPlaylist.AddAsync(playlist, tracks);
        }
    }

    private string Begin(Func<Task<IReadOnlyList<Track>>> load)
    {
        _token = Guid.NewGuid().ToString("N");
        _load = load;
        return _token;
    }

    /// <summary>The dragged tracks; empty for an unknown or stale token, and (reported) when loading fails.</summary>
    private async Task<IReadOnlyList<Track>> LoadAsync(string? token, string operation)
    {
        if (!IsCurrent(token) || _load is not { } load)
        {
            return [];
        }

        try
        {
            return await load();
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, operation);
            return [];
        }
    }
}
