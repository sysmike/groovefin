using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;

namespace Groovefin.Core.Playback;

/// <summary>
/// "Add to playlist" for any list of tracks: the playlists to offer, adding to one (after asking about songs
/// it already has, #312), and creating a new one with the tracks. Results and errors are shown as
/// notifications. Used by the album page, every context menu (#34) and drag and drop (#35).
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

    /// <summary>
    /// Appends <paramref name="tracks"/> to <paramref name="target"/>; when it already has some of them, the
    /// user chooses first whether to add them again, only the new ones, or nothing (#312).
    /// </summary>
    public async Task AddAsync(Playlist target, IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0 || await ChooseTracksAsync(target, tracks) is not { Count: > 0 } chosen)
        {
            return;
        }

        tracks = chosen;
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

    /// <summary>
    /// The tracks to add: all of them when the playlist has none of them yet, else what the user chooses
    /// (<see langword="null"/> for nothing). When its entries cannot be loaded, all are added: the question
    /// only helps.
    /// </summary>
    private async Task<IReadOnlyList<Track>?> ChooseTracksAsync(Playlist target, IReadOnlyList<Track> tracks)
    {
        HashSet<string> present;
        try
        {
            present = [.. (await playlists.GetEntriesAsync(target.Id)).Select(e => e.Track.Id)];
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            return tracks;
        }

        var duplicates = tracks.Where(t => present.Contains(t.Id)).ToList();
        if (duplicates.Count == 0)
        {
            return tracks;
        }

        var title = strings.GetString("Playlist_DuplicateTitle");
        var cancel = strings.GetString("Dialog_Cancel");
        if (duplicates.Count == tracks.Count)
        {
            var message = tracks.Count == 1
                ? Format("Playlist_DuplicateOne", tracks[0].Name, target.Name)
                : Format("Playlist_DuplicateAll", Count(tracks.Count), target.Name);
            return await dialogs.ConfirmAsync(title, message, strings.GetString("Playlist_AddAgain"), cancel) ? tracks : null;
        }

        var question = duplicates.Count == 1
            ? Format("Playlist_DuplicateSomeOne", duplicates[0].Name, target.Name)
            : Format("Playlist_DuplicateSome", Count(duplicates.Count), Count(tracks.Count), target.Name);
        var choice = await dialogs.ChooseAsync(title, question, strings.GetString("Playlist_AddNewOnly"), strings.GetString("Playlist_AddAll"), cancel);
        return choice switch
        {
            DialogChoice.Primary => [.. tracks.Where(t => !present.Contains(t.Id))],
            DialogChoice.Secondary => tracks,
            _ => null,
        };
    }

    /// <summary>The string <paramref name="key"/> with {0}, {1}, … replaced in order.</summary>
    private string Format(string key, params string[] values)
    {
        var text = strings.GetString(key);
        for (var i = 0; i < values.Length; i++)
        {
            text = text.Replace("{" + i.ToString(CultureInfo.InvariantCulture) + "}", values[i], StringComparison.Ordinal);
        }

        return text;
    }

    private static string Count(int count) => count.ToString(CultureInfo.CurrentCulture);

    private void Added(string playlistName, int count) =>
        notifications.Show(new AppNotification(
            NotificationSeverity.Success,
            strings.GetString("Playlist_AddedTitle"),
            strings.GetString(count == 1 ? "Playlist_AddedOne" : "Playlist_Added")
                .Replace("{0}", count.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{1}", playlistName, StringComparison.Ordinal)));
}
