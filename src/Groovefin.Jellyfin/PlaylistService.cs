using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Jellyfin.Mapping;
using Jellyfin.Sdk.Generated.Models;

namespace Groovefin.Jellyfin;

/// <summary><see cref="IPlaylistService"/> on Jellyfin's <c>/Playlists</c> endpoints for the active connection.</summary>
internal sealed class PlaylistService(JellyfinClient client) : IPlaylistService
{
    public async Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.IncludeItemTypes = [BaseItemKind.Playlist];
                    p.MediaTypes = [MediaType.Audio];
                    p.Recursive = true;
                    p.SortBy = [ItemSortBy.SortName];
                    p.Fields = [ItemFields.ChildCount];
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                    p.EnableTotalRecordCount = false;
                },
                cancellationToken),
            "Loading playlists").ConfigureAwait(false);

        return (result.Items ?? []).Select(ItemMapper.ToPlaylist).ToList();
    }

    public async Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        var id = ParseId(playlistId);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var dto = await JellyfinCall.RunAsync(
            () => api.Items[id].GetAsync(request => request.QueryParameters.UserId = userId, cancellationToken),
            "Loading playlist").ConfigureAwait(false);
        return ItemMapper.ToPlaylist(dto);
    }

    public async Task<IReadOnlyList<PlaylistEntry>> GetEntriesAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        var id = ParseId(playlistId);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Playlists[id].Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.EnableUserData = true;
                    // Primary only: the track DTO then carries AlbumPrimaryImageTag for covers.
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                },
                cancellationToken),
            "Loading playlist tracks").ConfigureAwait(false);

        return (result.Items ?? [])
            .Where(dto => !string.IsNullOrEmpty(dto.PlaylistItemId))
            .Select(dto => new PlaylistEntry(dto.PlaylistItemId!, ItemMapper.ToTrack(dto)))
            .ToList();
    }

    public async Task<string> CreateAsync(string name, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(trackIds);

        var (api, connection) = client.Active;
        var body = new CreatePlaylistDto
        {
            Name = name.Trim(),
            Ids = [.. trackIds.Select(id => new Guid?(ParseId(id)))],
            UserId = Guid.Parse(connection.UserId),
            MediaType = CreatePlaylistDto_MediaType.Audio,
        };

        var result = await JellyfinCall.RunAsync(
            () => api.Playlists.PostAsync(body, cancellationToken: cancellationToken),
            "Creating a playlist").ConfigureAwait(false);

        return result.Id is { Length: > 0 } id ? id : throw new JellyfinException("The server did not return the new playlist's ID.");
    }

    public Task RenameAsync(string playlistId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var id = ParseId(playlistId);
        var (api, _) = client.Active;

        var body = new UpdatePlaylistDto { Name = name.Trim() };
        return JellyfinCall.RunAsync(
            () => api.Playlists[id].PostAsync(body, cancellationToken: cancellationToken),
            "Renaming a playlist");
    }

    public Task DeleteAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        var id = ParseId(playlistId);
        var (api, _) = client.Active;

        return JellyfinCall.RunAsync(
            () => api.Items.DeleteAsync(request => request.QueryParameters.Ids = [id], cancellationToken),
            "Deleting a playlist");
    }

    public Task AddTracksAsync(string playlistId, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        var id = ParseId(playlistId);
        var ids = trackIds.Select(t => new Guid?(ParseId(t))).ToArray();
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        return JellyfinCall.RunAsync(
            () => api.Playlists[id].Items.PostAsync(
                request =>
                {
                    request.QueryParameters.Ids = ids;
                    request.QueryParameters.UserId = userId;
                },
                cancellationToken),
            "Adding to a playlist");
    }

    public Task RemoveEntriesAsync(string playlistId, IReadOnlyList<string> entryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        var id = ParseId(playlistId);
        var entries = entryIds.ToArray();
        var (api, _) = client.Active;

        return JellyfinCall.RunAsync(
            () => api.Playlists[id].Items.DeleteAsync(request => request.QueryParameters.EntryIds = entries, cancellationToken),
            "Removing from a playlist");
    }

    public Task MoveEntryAsync(string playlistId, string entryId, int newIndex, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        var id = ParseId(playlistId);
        var (api, _) = client.Active;

        return JellyfinCall.RunAsync(
            () => api.Playlists[id].Items[entryId].Move[newIndex].PostAsync(cancellationToken: cancellationToken),
            "Moving a playlist entry");
    }

    private static Guid ParseId(string id) =>
        Guid.TryParse(id, out var guid) ? guid : throw new ArgumentException($"'{id}' is not a Jellyfin item ID.", nameof(id));
}
