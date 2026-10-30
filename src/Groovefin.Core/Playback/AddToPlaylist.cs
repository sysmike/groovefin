using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;

namespace Groovefin.Core.Playback;

/// <summary>
/// "Add to playlist" for any list of tracks: the playlists to offer, adding to one, and creating a new
/// one with the tracks. Results and errors are shown as notifications. Used by the album page and, with
/// #34, by every context menu.
/// </summary>
public sealed class AddToPlaylist(
    IPlaylistService playlists,
    IDialogService dialogs,
    INotificationService notifications,
    IErrorReporter errors,
    IStringResources strings,
    IMessenger messenger)
{
    /// <summary>Gets the playlists to offer, by name; empty (and reported) when they cannot be loaded.</summary>
    public async Task<IReadOnlyList<Playlist>> GetTargetsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await playlists.GetPlaylistsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            errors.Report(ex, Operations.LoadPlaylists);
            return [];
        }
    }

    /// <summary>Appends <paramref name="tracks"/> to <paramref name="target"/>.</summary>
    public async Task AddAsync(Playlist target, IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0)
        {
            return;
        }

        try
        {
            await playlists.AddTracksAsync(target.Id, [.. tracks.Select(t => t.Id)]);
            messenger.Send(new PlaylistTracksAddedMessage(target.Id));
            Added(target.Name, tracks.Count);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.EditPlaylist);
        }
    }

    /// <summary>Asks for a name and creates a playlist with <paramref name="tracks"/>.</summary>
    public async Task CreateWithAsync(IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var name = await dialogs.PromptAsync(
            strings.GetString("Playlist_CreateTitle"),
            strings.GetString("Playlist_NameLabel"),
            string.Empty,
            strings.GetString("Playlist_CreateButton"),
            strings.GetString("Dialog_Cancel"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            await playlists.CreateAsync(name, [.. tracks.Select(t => t.Id)]);
            Added(name.Trim(), tracks.Count);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.EditPlaylist);
        }
    }

    private void Added(string playlistName, int count) =>
        notifications.Show(new AppNotification(
            NotificationSeverity.Success,
            strings.GetString("Playlist_AddedTitle"),
            strings.GetString(count == 1 ? "Playlist_AddedOne" : "Playlist_Added")
                .Replace("{0}", count.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{1}", playlistName, StringComparison.Ordinal)));
}
