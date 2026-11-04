using Groovefin.Core.Api;
using Groovefin.Core.Models;

namespace Groovefin.Core.Downloads;

/// <summary>
/// <see cref="IPlaylistService"/> for the pages: the server's, but when it cannot be reached the playlists and their
/// songs come from the downloads (#335). They then hold only downloaded songs, with the count and length of those.
/// Editing still needs the server.
/// </summary>
public sealed class OfflinePlaylistService(IPlaylistService server, OfflineAnswers answers) : IPlaylistService
{
    public Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetPlaylistsAsync(cancellationToken),
            index => (IReadOnlyList<Playlist>)[.. index.Playlists.Select(p => Downloaded(index, p)).OfType<Playlist>()],
            cancellationToken);

    public Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetPlaylistAsync(playlistId, cancellationToken),
            index => index.Playlists.FirstOrDefault(p => p.Playlist.Id == playlistId) is { } playlist ? Downloaded(index, playlist) : null,
            cancellationToken);

    /// <summary>Offline, the entries have made-up IDs: the server's are not kept, and editing needs the server anyway.</summary>
    public Task<IReadOnlyList<PlaylistEntry>> GetEntriesAsync(string playlistId, CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetEntriesAsync(playlistId, cancellationToken),
            index => index.Playlists.FirstOrDefault(p => p.Playlist.Id == playlistId) is { } playlist
                ? (IReadOnlyList<PlaylistEntry>)[.. playlist.TrackIds
                    .Where(index.Tracks.ContainsKey)
                    .Select((id, i) => new PlaylistEntry($"offline-{i}", index.Tracks[id].Track))]
                : null,
            cancellationToken);

    public Task<string> CreateAsync(string name, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default) =>
        server.CreateAsync(name, trackIds, cancellationToken);

    public Task RenameAsync(string playlistId, string name, CancellationToken cancellationToken = default) =>
        server.RenameAsync(playlistId, name, cancellationToken);

    public Task DeleteAsync(string playlistId, CancellationToken cancellationToken = default) =>
        server.DeleteAsync(playlistId, cancellationToken);

    public Task AddTracksAsync(string playlistId, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default) =>
        server.AddTracksAsync(playlistId, trackIds, cancellationToken);

    public Task RemoveEntriesAsync(string playlistId, IReadOnlyList<string> entryIds, CancellationToken cancellationToken = default) =>
        server.RemoveEntriesAsync(playlistId, entryIds, cancellationToken);

    public Task MoveEntryAsync(string playlistId, string entryId, int newIndex, CancellationToken cancellationToken = default) =>
        server.MoveEntryAsync(playlistId, entryId, newIndex, cancellationToken);

    /// <summary>The playlist with the count and length of its downloaded songs, or <see langword="null"/> without any.</summary>
    private static Playlist? Downloaded(DownloadIndex index, DownloadedPlaylist playlist)
    {
        var songs = OfflineAnswers.Downloaded(index, playlist.TrackIds);
        return songs.Count == 0
            ? null
            : playlist.Playlist with
            {
                TrackCount = songs.Count,
                Duration = TimeSpan.FromTicks(songs.Sum(t => t.Duration?.Ticks ?? 0)),
            };
    }
}
