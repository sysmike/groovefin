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

/// <summary>A track row: cover, title, artists · album, length.</summary>
public sealed partial class TrackRow(Track track, ImageRequest? cover) : ObservableObject, ICurrentTrackRow
{
    public Track Track { get; } = track;

    public ImageRequest? Cover { get; } = cover;

    /// <summary>Gets or sets whether the row's track is playing (#247).</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public string Name => Track.Name;

    public string Details => string.Join(" · ", new[] { string.Join(", ", Track.Artists.Select(a => a.Name)), Track.AlbumName ?? string.Empty }.Where(s => s.Length > 0));

    public string Duration => Durations.Track(Track.Duration);

    public string AccessibleName => string.Join(", ", new[] { Name, Details, Duration }.Where(s => s.Length > 0));
}

/// <summary>
/// Genre page: play, shuffle or mix the genre, its albums and its songs. Play and shuffle use up to
/// <see cref="TrackLimit"/> songs; the page lists the first <see cref="TracksShown"/>.
/// </summary>
public sealed partial class GenreDetailViewModel(
    ILibraryService library,
    IPlayer player,
    INavigationService navigation,
    IErrorReporter errors,
    InstantMix instantMix,
    CurrentTrack currentTrack) : ObservableObject
{
    public const int TrackLimit = 1000;

    public const int TracksShown = 100;

    public const int AlbumLimit = 200;

    public const double TileWidth = 152;

    public const double CoverWidth = 40;

    private IReadOnlyList<Track> _tracks = [];

    public ObservableCollection<AlbumItem> Albums { get; } = [];

    public ObservableCollection<TrackRow> Tracks { get; } = currentTrack.Watch(new ObservableCollection<TrackRow>());

    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(HasGenre))]
    [NotifyCanExecuteChangedFor(nameof(InstantMixCommand))]
    public partial Genre? Genre { get; private set; }

    public bool HasGenre => Genre is not null;

    public string Name => Genre?.Name ?? string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    [ObservableProperty]
    public partial bool HasAlbums { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(ShuffleCommand))]
    public partial bool HasTracks { get; private set; }

    public async Task LoadAsync(string genreId, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        LoadFailed = false;
        try
        {
            var genre = library.GetGenreAsync(genreId, cancellationToken);
            var albums = library.GetAlbumsAsync(new AlbumQuery { GenreIds = [genreId], Limit = AlbumLimit }, cancellationToken);
            var tracks = library.GetGenreTracksAsync(genreId, TrackLimit, cancellationToken);
            await Task.WhenAll(genre, albums, tracks);

            Genre = await genre;
            Albums.Clear();
            foreach (var album in (await albums).Items)
            {
                Albums.Add(new AlbumItem(album, ImageRequest.Primary(album.Id, album.PrimaryImageTag, TileWidth, DisplayScale)));
            }

            _tracks = await tracks;
            Tracks.Clear();
            foreach (var track in _tracks.Take(TracksShown))
            {
                var cover = track.AlbumId is { } albumId ? ImageRequest.Primary(albumId, track.AlbumImageTag, CoverWidth, DisplayScale) : null;
                Tracks.Add(new TrackRow(track, cover));
            }

            HasAlbums = Albums.Count > 0;
            HasTracks = _tracks.Count > 0;
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadGenre);
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

    [RelayCommand(CanExecute = nameof(HasTracks))]
    private void Play() => player.Play(_tracks);

    [RelayCommand(CanExecute = nameof(HasTracks))]
    private void Shuffle() => player.Shuffle(_tracks);

    /// <summary>Plays the genre's songs from <paramref name="row"/> on.</summary>
    [RelayCommand]
    private void PlayTrack(TrackRow row)
    {
        var index = _tracks.ToList().IndexOf(row.Track);
        player.Play(_tracks, Math.Max(0, index));
    }

    [RelayCommand(CanExecute = nameof(HasGenre))]
    private Task InstantMixAsync() => instantMix.StartAsync(Genre!.Id);

    [RelayCommand]
    private void OpenAlbum(AlbumItem item) => navigation.NavigateTo(PageKeys.Album, item.Album.Id);
}
