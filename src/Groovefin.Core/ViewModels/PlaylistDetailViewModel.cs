using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Formatting;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>A row of a playlist: cover, title, artists · album, length.</summary>
public sealed record PlaylistEntryItem(PlaylistEntry Entry, ImageRequest? Cover)
{
    public string Name => Entry.Track.Name;

    public string Details => string.Join(" · ", new[] { string.Join(", ", Entry.Track.Artists.Select(a => a.Name)), Entry.Track.AlbumName ?? string.Empty }.Where(s => s.Length > 0));

    public string Duration => Durations.Track(Entry.Track.Duration);

    public string AccessibleName => string.Join(", ", new[] { Name, Details, Duration }.Where(s => s.Length > 0));
}

/// <summary>
/// Playlist detail page: play, shuffle, queue, rename, delete (after a confirmation), and edit the
/// entries (remove, move up/down, drag). Edits show at once and are sent to the server; when the server
/// refuses, the entries are loaded again and the error is shown.
/// </summary>
public sealed partial class PlaylistDetailViewModel(
    IPlaylistService playlists,
    IPlayer player,
    INavigationService navigation,
    IDialogService dialogs,
    IErrorReporter errors,
    IStringResources strings) : ObservableObject
{
    public const double ImageWidth = 200;

    public const double CoverWidth = 40;

    public ObservableCollection<PlaylistEntryItem> Entries { get; } = [];

    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaylist), nameof(Name), nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(DeleteCommand))]
    public partial Playlist? Playlist { get; private set; }

    public bool HasPlaylist => Playlist is not null;

    public string Name => Playlist?.Name ?? string.Empty;

    [ObservableProperty]
    public partial ImageRequest? Image { get; private set; }

    /// <summary>Gets "12 tracks · 1:02:15" for the entries shown.</summary>
    [ObservableProperty]
    public partial string Details { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(ShuffleCommand), nameof(AddToQueueCommand))]
    public partial bool HasEntries { get; private set; }

    /// <summary>Gets a value indicating whether the loaded playlist has no entries.</summary>
    public bool IsEmpty => HasPlaylist && !HasEntries;

    public async Task LoadAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        LoadFailed = false;
        try
        {
            var playlistTask = playlists.GetPlaylistAsync(playlistId, cancellationToken);
            var entriesTask = playlists.GetEntriesAsync(playlistId, cancellationToken);
            var playlist = await playlistTask;
            var entries = await entriesTask;

            Playlist = playlist;
            Image = ImageRequest.Primary(playlist.Id, playlist.PrimaryImageTag, ImageWidth, DisplayScale);
            ShowEntries(entries);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadPlaylist);
        }
        catch (OperationCanceledException)
        {
            // The page was closed.
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Sends a move the list already made (a drag): <paramref name="item"/> is now at
    /// <paramref name="newIndex"/> in <see cref="Entries"/>.
    /// </summary>
    public Task MoveEntryAsync(PlaylistEntryItem item, int newIndex) =>
        EditAsync(() => playlists.MoveEntryAsync(Playlist!.Id, item.Entry.EntryId, newIndex));

    private IReadOnlyList<Track> Tracks => [.. Entries.Select(e => e.Entry.Track)];

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void Play() => player.Play(Tracks);

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void Shuffle() => player.Shuffle(Tracks);

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void AddToQueue() => player.AddToQueue(Tracks);

    /// <summary>Plays the playlist from <paramref name="item"/> on (a row was clicked).</summary>
    [RelayCommand]
    private void PlayEntry(PlaylistEntryItem item) => player.Play(Tracks, Math.Max(0, Entries.IndexOf(item)));

    [RelayCommand(CanExecute = nameof(HasPlaylist))]
    private async Task RenameAsync()
    {
        var playlist = Playlist!;
        var name = await dialogs.PromptAsync(
            strings.GetString("Playlist_RenameTitle"),
            strings.GetString("Playlist_NameLabel"),
            playlist.Name,
            strings.GetString("Dialog_Save"),
            strings.GetString("Dialog_Cancel"));
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == playlist.Name)
        {
            return;
        }

        try
        {
            await playlists.RenameAsync(playlist.Id, name);
            Playlist = playlist with { Name = name.Trim() };
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            errors.Report(ex, Operations.EditPlaylist);
        }
    }

    /// <summary>Deletes the playlist after a confirmation and goes back.</summary>
    [RelayCommand(CanExecute = nameof(HasPlaylist))]
    private async Task DeleteAsync()
    {
        var playlist = Playlist!;
        var confirmed = await dialogs.ConfirmAsync(
            strings.GetString("Playlist_DeleteTitle"),
            strings.GetString("Playlist_DeleteMessage").Replace("{0}", playlist.Name, StringComparison.Ordinal),
            strings.GetString("Playlist_DeleteConfirm"),
            strings.GetString("Dialog_Cancel"));
        if (!confirmed)
        {
            return;
        }

        try
        {
            await playlists.DeleteAsync(playlist.Id);
            Playlist = null;
            navigation.GoBack();
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            errors.Report(ex, Operations.EditPlaylist);
        }
    }

    [RelayCommand]
    private Task RemoveAsync(PlaylistEntryItem item)
    {
        if (!Entries.Remove(item))
        {
            return Task.CompletedTask;
        }

        UpdateDetails();
        return EditAsync(() => playlists.RemoveEntriesAsync(Playlist!.Id, [item.Entry.EntryId]));
    }

    [RelayCommand]
    private Task MoveUpAsync(PlaylistEntryItem item) => MoveByAsync(item, -1);

    [RelayCommand]
    private Task MoveDownAsync(PlaylistEntryItem item) => MoveByAsync(item, 1);

    private Task MoveByAsync(PlaylistEntryItem item, int offset)
    {
        var index = Entries.IndexOf(item);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Entries.Count)
        {
            return Task.CompletedTask;
        }

        Entries.Move(index, target);
        return MoveEntryAsync(item, target);
    }

    /// <summary>Sends an edit the list already shows; on failure reloads the entries and reports.</summary>
    private async Task EditAsync(Func<Task> edit)
    {
        if (Playlist is not { } playlist)
        {
            return;
        }

        try
        {
            await edit();
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.EditPlaylist);
            try
            {
                ShowEntries(await playlists.GetEntriesAsync(playlist.Id));
            }
            catch (Exception reload) when (reload is JellyfinException or InvalidOperationException)
            {
                // The edit's error is already shown; the list stays as the user left it.
            }
        }
    }

    private void ShowEntries(IReadOnlyList<PlaylistEntry> entries)
    {
        Entries.Clear();
        foreach (var entry in entries)
        {
            var track = entry.Track;
            var cover = track.AlbumId is { } albumId ? ImageRequest.Primary(albumId, track.AlbumImageTag, CoverWidth, DisplayScale) : null;
            Entries.Add(new PlaylistEntryItem(entry, cover));
        }

        UpdateDetails();
    }

    private void UpdateDetails()
    {
        HasEntries = Entries.Count > 0;
        var known = Entries.Select(e => e.Entry.Track.Duration).Where(d => d is not null).Select(d => d!.Value).ToList();
        TimeSpan? total = known.Count > 0 ? known.Aggregate(TimeSpan.Zero, (sum, d) => sum + d) : null;
        Details = PlaylistItem.DetailsText(Entries.Count, total, strings);
    }
}
