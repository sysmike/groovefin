using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>One step of the folder path; the top has no ID.</summary>
public sealed record FolderCrumb(string? Id, string Name);

/// <summary>
/// Folders page: the library as it is on disk. A breadcrumb shows the path from the top; the open folder
/// lists its subfolders, then its tracks. Play and add to queue take the folder with all subfolders (up
/// to <see cref="TrackLimit"/> tracks).
/// </summary>
public sealed partial class FoldersViewModel(ILibraryService library, IPlayer player, IErrorReporter errors, IStringResources strings) : ObservableObject
{
    public const int TrackLimit = 1000;

    public const double CoverWidth = 40;

    private int _generation;

    /// <summary>Gets the path from the top to the open folder.</summary>
    public ObservableCollection<FolderCrumb> Path { get; } = [];

    public ObservableCollection<Folder> Folders { get; } = [];

    public ObservableCollection<TrackRow> Tracks { get; } = [];

    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool LoadFailed { get; private set; }

    /// <summary>Gets a value indicating whether a folder is open (not the top), so it can be played.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(AddToQueueCommand), nameof(GoUpCommand))]
    public partial bool IsInFolder { get; private set; }

    [ObservableProperty]
    public partial bool HasFolders { get; private set; }

    [ObservableProperty]
    public partial bool HasTracks { get; private set; }

    /// <summary>Gets a value indicating whether the open folder loaded and holds nothing.</summary>
    public bool IsEmpty => !IsLoading && !LoadFailed && Folders.Count == 0 && Tracks.Count == 0;

    /// <summary>Shows the top. Called when the page is opened from the navigation pane.</summary>
    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Path.Clear();
        Path.Add(new FolderCrumb(null, strings.GetString("Folders_Top")));
        return ShowAsync(cancellationToken);
    }

    [RelayCommand]
    private Task OpenAsync(Folder folder)
    {
        Path.Add(new FolderCrumb(folder.Id, folder.Name));
        return ShowAsync();
    }

    /// <summary>Goes back to a folder of the path.</summary>
    [RelayCommand]
    private Task GoToAsync(FolderCrumb crumb)
    {
        var index = Path.IndexOf(crumb);
        if (index < 0 || index == Path.Count - 1)
        {
            return Task.CompletedTask;
        }

        while (Path.Count > index + 1)
        {
            Path.RemoveAt(Path.Count - 1);
        }

        return ShowAsync();
    }

    [RelayCommand(CanExecute = nameof(IsInFolder))]
    private Task GoUpAsync() => GoToAsync(Path[^2]);

    /// <summary>Plays the open folder with its subfolders.</summary>
    [RelayCommand(CanExecute = nameof(IsInFolder))]
    private Task PlayAsync() => WithFolderTracksAsync(t => player.Play(t));

    [RelayCommand(CanExecute = nameof(IsInFolder))]
    private Task AddToQueueAsync() => WithFolderTracksAsync(player.AddToQueue);

    /// <summary>Plays the open folder's own tracks from <paramref name="row"/> on.</summary>
    [RelayCommand]
    private void PlayTrack(TrackRow row) => player.Play([.. Tracks.Select(t => t.Track)], Math.Max(0, Tracks.IndexOf(row)));

    private async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        var generation = ++_generation;
        var folderId = Path[^1].Id;
        IsInFolder = folderId is not null;
        IsLoading = true;
        LoadFailed = false;
        Folders.Clear();
        Tracks.Clear();
        HasFolders = HasTracks = false;
        try
        {
            var content = await library.GetFolderAsync(folderId, cancellationToken);

            // The user went elsewhere while this folder was loading.
            if (generation != _generation)
            {
                return;
            }

            foreach (var folder in content.Folders)
            {
                Folders.Add(folder);
            }

            foreach (var track in content.Tracks)
            {
                var cover = track.AlbumId is { } albumId ? ImageRequest.Primary(albumId, track.AlbumImageTag, CoverWidth, DisplayScale) : null;
                Tracks.Add(new TrackRow(track, cover));
            }

            HasFolders = Folders.Count > 0;
            HasTracks = Tracks.Count > 0;
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            if (generation == _generation)
            {
                LoadFailed = true;
                errors.Report(ex, Operations.LoadFolder);
            }
        }
        catch (OperationCanceledException)
        {
            // The page was closed.
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
            }
        }
    }

    private async Task WithFolderTracksAsync(Action<IReadOnlyList<Track>> use)
    {
        if (Path[^1].Id is not { } folderId)
        {
            return;
        }

        try
        {
            var tracks = await library.GetFolderTracksAsync(folderId, TrackLimit);
            if (tracks.Count > 0)
            {
                use(tracks);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.Playback);
        }
    }
}
