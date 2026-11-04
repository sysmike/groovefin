using Groovefin.Core.Api;

namespace Groovefin.Core.Downloads;

/// <summary>
/// The signed-in user's download index as the offline pages read it (#335): from disk, and again only after it
/// was saved. It does not use <see cref="DownloadManager"/>, which reads the server through the same services.
/// </summary>
public sealed class OfflineCatalog(IJellyfinClient client, IDownloadStore store)
{
    private readonly Lock _gate = new();
    private (string Key, DateTime SavedAt, DownloadIndex Index)? _cached;

    /// <summary>Gets the index, or <see langword="null"/> when not signed in or nothing was downloaded.</summary>
    public async Task<DownloadIndex?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (client.Connection?.UserDataKey is not { } key || store.IndexSavedAt(key) is not { } savedAt)
        {
            return null;
        }

        lock (_gate)
        {
            if (_cached is { } cached && cached.Key == key && cached.SavedAt == savedAt)
            {
                return cached.Index;
            }
        }

        var index = await store.LoadAsync(key, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _cached = (key, savedAt, index);
        }

        return index;
    }
}
