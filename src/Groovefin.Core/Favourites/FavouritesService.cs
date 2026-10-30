using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Notifications;

namespace Groovefin.Core.Favourites;

/// <summary>An item became a favourite, or stopped being one. Sent after the server saved it.</summary>
public sealed record FavouriteChangedMessage(string ItemId, bool IsFavourite);

/// <summary>
/// Favourites for tracks, albums and artists. Saves the change on the server and then tells every view
/// (<see cref="FavouriteChangedMessage"/>), so a heart toggled in one place is right everywhere. Views
/// show the new state at once and roll back when <see cref="SetAsync"/> returns <see langword="false"/>.
/// Use it from the UI thread.
/// </summary>
public sealed class FavouritesService(ILibraryService library, IMessenger messenger, IErrorReporter errors)
{
    /// <summary>States saved in this session; models loaded earlier (e.g. queued tracks) may predate them.</summary>
    private readonly Dictionary<string, bool> _saved = new(StringComparer.Ordinal);

    /// <summary>Gets the item's state: the one saved in this session, otherwise <paramref name="loaded"/>.</summary>
    public bool IsFavourite(string itemId, bool loaded) => _saved.TryGetValue(itemId, out var saved) ? saved : loaded;

    /// <summary>Saves the favourite state; returns <see langword="false"/> (and shows the error) when the server refuses.</summary>
    public async Task<bool> SetAsync(string itemId, bool isFavourite)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemId);
        try
        {
            await library.SetFavouriteAsync(itemId, isFavourite);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.SetFavourite);
            return false;
        }

        _saved[itemId] = isFavourite;
        messenger.Send(new FavouriteChangedMessage(itemId, isFavourite));
        return true;
    }

    /// <summary>
    /// Saves the state for several items, one after the other (#295); stops at the first the server refuses,
    /// so a lost connection shows one error, not one per item. Returns <see langword="false"/> then.
    /// </summary>
    public async Task<bool> SetAllAsync(IReadOnlyList<string> itemIds, bool isFavourite)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        foreach (var itemId in itemIds)
        {
            if (!await SetAsync(itemId, isFavourite))
            {
                return false;
            }
        }

        return true;
    }
}
