using Groovefin.Core.Settings;

namespace Groovefin.Core.Downloads;

/// <summary>
/// The playlists, albums and artists the signed-in user chose to download one by one (#387): the Download toggle on
/// their pages and in their menus. Kept in the settings for each server and user, not as favourites on the server.
/// Change them on the UI thread; <see cref="DownloadChoiceChangedMessage"/> tells the pages.
/// </summary>
public interface IDownloadChoices
{
    /// <summary>Gets a value indicating whether the item is chosen to download.</summary>
    bool IsChosen(DownloadItemKind kind, string id);

    /// <summary>
    /// Chooses the item, or not. A chosen item downloads at the next sync, shortly after, also when it was removed on
    /// the Downloads page. Unchosen, its files go, unless a collection switch or another choice still wants them.
    /// </summary>
    void SetChosen(DownloadItemKind kind, string id, string name, bool chosen);
}

/// <summary>Sent after an item was chosen to download, or no longer (#387).</summary>
public sealed record DownloadChoiceChangedMessage(DownloadItemKind Kind, string Id, bool IsChosen);
