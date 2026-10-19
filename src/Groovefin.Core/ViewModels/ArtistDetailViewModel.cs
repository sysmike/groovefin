using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Formatting;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>One row in an artist's top tracks.</summary>
public sealed record ArtistTrackItem(Track Track, int Rank)
{
    public string Number => Rank.ToString(CultureInfo.CurrentCulture);

    public string Name => Track.Name;

    public string Album => Track.AlbumName ?? string.Empty;

    public string Duration => Durations.Track(Track.Duration);

    /// <summary>Gets the text Narrator reads, e.g. "1, One More Time, Discovery, 5:20".</summary>
    public string AccessibleName =>
        string.Join(", ", new[] { Number, Name, Album, Duration }.Where(s => s.Length > 0));
}

/// <summary>
/// Artist detail page: image, genres, biography, top tracks, albums, albums the artist appears on and
/// similar artists. The header must load; the sections load in parallel and a failing section stays
/// hidden (reported once).
/// </summary>
public sealed partial class ArtistDetailViewModel(
    ILibraryService library,
    INavigationService navigation,
    IErrorReporter errors) : ObservableObject
{
    public const double ImageWidth = 200;

    /// <summary>Background width: small on purpose, it is blurred and stretched.</summary>
    public const double BackgroundWidth = 128;

    public const double TileWidth = 152;

    public const int TopTrackCount = 10;

    public const int SimilarCount = 12;

    /// <summary>Most albums shown per section; artists with more are rare.</summary>
    public const int AlbumLimit = 200;

    public ObservableCollection<ArtistTrackItem> TopTracks { get; } = [];

    public ObservableCollection<AlbumItem> Albums { get; } = [];

    public ObservableCollection<AlbumItem> AppearsOn { get; } = [];

    public ObservableCollection<ArtistItem> Similar { get; } = [];

    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtist), nameof(Name), nameof(GenresText), nameof(Biography), nameof(HasBiography))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavouriteCommand))]
    public partial Artist? Artist { get; private set; }

    public bool HasArtist => Artist is not null;

    public string Name => Artist?.Name ?? string.Empty;

    public string GenresText => Artist is null ? string.Empty : string.Join(" · ", Artist.Genres);

    public string Biography => Artist?.Biography ?? string.Empty;

    public bool HasBiography => !string.IsNullOrEmpty(Artist?.Biography);

    [ObservableProperty]
    public partial ImageRequest? Image { get; private set; }

    [ObservableProperty]
    public partial ImageRequest? Background { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Gets a value indicating whether the artist could not be loaded; the page shows a message instead.</summary>
    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    [ObservableProperty]
    public partial bool IsFavourite { get; private set; }

    [ObservableProperty]
    public partial bool HasTopTracks { get; private set; }

    [ObservableProperty]
    public partial bool HasAlbums { get; private set; }

    [ObservableProperty]
    public partial bool HasAppearsOn { get; private set; }

    [ObservableProperty]
    public partial bool HasSimilar { get; private set; }

    public async Task LoadAsync(string artistId, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        LoadFailed = false;
        try
        {
            var artistTask = library.GetArtistAsync(artistId, cancellationToken);
            var sections = Task.WhenAll(
                LoadSectionAsync(() => LoadTopTracksAsync(artistId, cancellationToken)),
                LoadSectionAsync(() => LoadAlbumsAsync(artistId, cancellationToken)),
                LoadSectionAsync(() => LoadAppearsOnAsync(artistId, cancellationToken)),
                LoadSectionAsync(() => LoadSimilarAsync(artistId, cancellationToken)));

            var artist = await artistTask;
            Artist = artist;
            IsFavourite = artist.IsFavorite;
            Image = ImageRequest.Primary(artist.Id, artist.PrimaryImageTag, ImageWidth, DisplayScale);
            Background = ImageRequest.Primary(artist.Id, artist.PrimaryImageTag, BackgroundWidth, 1);

            var failures = await sections;
            if (!cancellationToken.IsCancellationRequested && failures.FirstOrDefault(f => f is not null) is { } failure)
            {
                errors.Report(failure, Operations.LoadArtist);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadArtist);
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

    [RelayCommand(CanExecute = nameof(HasArtist))]
    private async Task ToggleFavouriteAsync()
    {
        var artist = Artist!;
        var target = !IsFavourite;

        // Optimistic: show the new state at once, roll back if the server refuses.
        IsFavourite = target;
        try
        {
            await library.SetFavouriteAsync(artist.Id, target);
            Artist = artist with { IsFavorite = target };
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            IsFavourite = !target;
            errors.Report(ex, Operations.SetFavourite);
        }
    }

    // TODO(#30): play and shuffle the artist once the player service exists (engine #25, queue #26, stream URLs #28).
    private static bool CanPlay() => false;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Shuffle()
    {
    }

    [RelayCommand]
    private void OpenAlbum(AlbumItem item) => navigation.NavigateTo(PageKeys.Album, item.Album.Id);

    [RelayCommand]
    private void OpenTrackAlbum(ArtistTrackItem item)
    {
        if (item.Track.AlbumId is { } albumId)
        {
            navigation.NavigateTo(PageKeys.Album, albumId);
        }
    }

    [RelayCommand]
    private void OpenArtist(ArtistItem item) => navigation.NavigateTo(PageKeys.Artist, item.Artist.Id);

    /// <summary>Runs one section load; returns its failure so the page reports it once.</summary>
    private static async Task<Exception?> LoadSectionAsync(Func<Task> load)
    {
        try
        {
            await load();
            return null;
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            return ex;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task LoadTopTracksAsync(string artistId, CancellationToken cancellationToken)
    {
        var tracks = await library.GetArtistTopTracksAsync(artistId, TopTrackCount, cancellationToken);
        TopTracks.Clear();
        for (var i = 0; i < tracks.Count; i++)
        {
            TopTracks.Add(new ArtistTrackItem(tracks[i], i + 1));
        }

        HasTopTracks = TopTracks.Count > 0;
    }

    private async Task LoadAlbumsAsync(string artistId, CancellationToken cancellationToken)
    {
        var page = await library.GetAlbumsAsync(
            new AlbumQuery { AlbumArtistId = artistId, SortBy = AlbumSortField.Year, Descending = true, Limit = AlbumLimit },
            cancellationToken);
        Fill(Albums, page.Items);
        HasAlbums = Albums.Count > 0;
    }

    private async Task LoadAppearsOnAsync(string artistId, CancellationToken cancellationToken)
    {
        var page = await library.GetAlbumsAsync(
            new AlbumQuery { ContributingArtistId = artistId, SortBy = AlbumSortField.Year, Descending = true, Limit = AlbumLimit },
            cancellationToken);

        // The server already leaves out albums by the artist; this keeps the two sections disjoint either way.
        Fill(AppearsOn, page.Items.Where(a => a.AlbumArtists.All(r => r.Id != artistId)));
        HasAppearsOn = AppearsOn.Count > 0;
    }

    private async Task LoadSimilarAsync(string artistId, CancellationToken cancellationToken)
    {
        var artists = await library.GetSimilarArtistsAsync(artistId, SimilarCount, cancellationToken);
        Similar.Clear();
        foreach (var artist in artists)
        {
            Similar.Add(new ArtistItem(artist, ImageRequest.Primary(artist.Id, artist.PrimaryImageTag, TileWidth, DisplayScale)));
        }

        HasSimilar = Similar.Count > 0;
    }

    private void Fill(ObservableCollection<AlbumItem> target, IEnumerable<Album> albums)
    {
        target.Clear();
        foreach (var album in albums)
        {
            target.Add(new AlbumItem(album, ImageRequest.Primary(album.Id, album.PrimaryImageTag, TileWidth, DisplayScale)));
        }
    }
}
