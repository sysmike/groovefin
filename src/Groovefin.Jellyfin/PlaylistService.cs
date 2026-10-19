using Groovefin.Core.Api;
using Jellyfin.Sdk.Generated.Models;

namespace Groovefin.Jellyfin;

/// <summary><see cref="IPlaylistService"/> on Jellyfin's <c>/Playlists</c> endpoints for the active connection.</summary>
internal sealed class PlaylistService(JellyfinClient client) : IPlaylistService
{
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

    private static Guid ParseId(string id) =>
        Guid.TryParse(id, out var guid) ? guid : throw new ArgumentException($"'{id}' is not a Jellyfin item ID.", nameof(id));
}
