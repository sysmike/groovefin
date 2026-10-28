using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Formatting;
using Groovefin.Core.Images;
using Groovefin.Core.Links;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>One row in an artist's top tracks.</summary>
public sealed partial class ArtistTrackItem(Track track, int rank) : ObservableObject, ICurrentTrackRow
{
    public Track Track { get; } = track;

    public int Rank { get; } = rank;

    /// <summary>Gets or sets whether the row's track is playing (#247).</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

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
    IPlayer player,
    INavigationService navigation,
    IErrorReporter errors,
    FavouritesService favourites,
    IMessenger messenger,
    InstantMix instantMix,
    CurrentTrack currentTrack) : ObservableObject
{
    public const double ImageWidth = 200;

    /// <summary>Background width: small on purpose, it is blurred and stretched.</summary>
    public const double BackgroundWidth = 128;

    public const double TileWidth = 152;

    /// <summary>Least width of the albums grid's tiles (the page's <c>AlbumTile</c>), as on the Albums page (#263); they grow to fill each row.</summary>
    public const double AlbumGridTileWidth = 168;

    public const int TopTrackCount = 10;

    /// <summary>Similar artists loaded: more than fit in one row of a usual window; <see cref="SimilarRow"/> shows the first ones.</summary>
    public const int SimilarCount = 16;

    /// <summary>Most albums shown per section; artists with more are rare.</summary>
    public const int AlbumLimit = 200;

    private int _similarColumns = SimilarCount;

    public ObservableCollection<ArtistTrackItem> TopTracks { get; } = currentTrack.Watch(new ObservableCollection<ArtistTrackItem>());

    public ObservableCollection<AlbumItem> Albums { get; } = [];

    public ObservableCollection<AlbumItem> AppearsOn { get; } = [];

    public ObservableCollection<ArtistItem> Similar { get; } = [];

    /// <summary>Gets the similar artists the page shows: as many as fit in one row (#264).</summary>
    public ObservableCollection<ArtistItem> SimilarRow { get; } = [];

    /// <summary>
    /// Gets or sets how many tiles fit in the similar artists' row; the page sets it when the row resizes,
    /// and <see cref="SimilarRow"/> follows. Until then the row shows them all.
    /// </summary>
    public int SimilarColumns
    {
        get => _similarColumns;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            if (_similarColumns != value)
            {
                _similarColumns = value;
                FillSimilarRow();
            }
        }
    }

    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtist), nameof(Name), nameof(GenresText), nameof(Biography), nameof(HasBiography))]
    [NotifyPropertyChangedFor(nameof(MusicBrainzUrl), nameof(HasMusicBrainz), nameof(LastFmUrl), nameof(HasLastFm))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavouriteCommand), nameof(PlayCommand), nameof(ShuffleCommand), nameof(InstantMixCommand))]
    public partial Artist? Artist { get; private set; }

    public bool HasArtist => Artist is not null;

    /// <summary>Gets the artist on MusicBrainz, when the server knows its ID.</summary>
    public Uri? MusicBrainzUrl => Artist is null ? null : ExternalLinks.MusicBrainz(Artist);

    public bool HasMusicBrainz => MusicBrainzUrl is not null;

    /// <summary>Gets the artist on Last.fm.</summary>
    public Uri? LastFmUrl => Artist is null ? null : ExternalLinks.LastFm(Artist);

    public bool HasLastFm => LastFmUrl is not null;

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
        // Weak: the page's view model needs no unregistering.
        if (!messenger.IsRegistered<FavouriteChangedMessage>(this))
        {
            messenger.Register<ArtistDetailViewModel, FavouriteChangedMessage>(this, static (r, m) => r.OnFavouriteChanged(m));
        }

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
            IsFavourite = favourites.IsFavourite(artist.Id, artist.IsFavorite);
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
        if (await favourites.SetAsync(artist.Id, target))
        {
            Artist = artist with { IsFavorite = target };
        }
        else
        {
            IsFavourite = !target;
        }
    }

    /// <summary>A heart was toggled elsewhere (favourites page, a context menu).</summary>
    private void OnFavouriteChanged(FavouriteChangedMessage message)
    {
        if (Artist?.Id == message.ItemId)
        {
            IsFavourite = message.IsFavourite;
        }
    }

    /// <summary>Plays all of the artist's tracks, album by album.</summary>
    [RelayCommand(CanExecute = nameof(HasArtist))]
    private Task PlayAsync() => PlayAllAsync(shuffle: false);

    [RelayCommand(CanExecute = nameof(HasArtist))]
    private Task ShuffleAsync() => PlayAllAsync(shuffle: true);

    [RelayCommand(CanExecute = nameof(HasArtist))]
    private Task InstantMixAsync() => instantMix.StartAsync(Artist!.Id);

    [RelayCommand]
    private void OpenAlbum(AlbumItem item) => navigation.NavigateTo(PageKeys.Album, item.Album.Id);

    /// <summary>Plays the top tracks from <paramref name="item"/> on (a top track was clicked).</summary>
    [RelayCommand]
    private void PlayTopTrack(ArtistTrackItem item)
    {
        var tracks = TopTracks.Select(t => t.Track).ToList();
        player.Play(tracks, item.Rank - 1);
    }

    [RelayCommand]
    private void OpenArtist(ArtistItem item) => navigation.NavigateTo(PageKeys.Artist, item.Artist.Id);

    private async Task PlayAllAsync(bool shuffle)
    {
        try
        {
            var tracks = await library.GetArtistTracksAsync(Artist!.Id);
            if (shuffle)
            {
                player.Shuffle(tracks);
            }
            else
            {
                player.Play(tracks);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.Playback);
        }
    }

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
        Fill(Albums, page.Items, AlbumGridTileWidth);
        HasAlbums = Albums.Count > 0;
    }

    private async Task LoadAppearsOnAsync(string artistId, CancellationToken cancellationToken)
    {
        var page = await library.GetAlbumsAsync(
            new AlbumQuery { ContributingArtistId = artistId, SortBy = AlbumSortField.Year, Descending = true, Limit = AlbumLimit },
            cancellationToken);

        // The server already leaves out albums by the artist; this keeps the two sections disjoint either way.
        Fill(AppearsOn, page.Items.Where(a => a.AlbumArtists.All(r => r.Id != artistId)), TileWidth);
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
        SimilarRow.Clear();
        FillSimilarRow();
    }

    /// <summary>Adds or removes tiles at the row's end only, so the ones that stay keep their containers.</summary>
    private void FillSimilarRow()
    {
        var count = Math.Min(_similarColumns, Similar.Count);
        while (SimilarRow.Count > count)
        {
            SimilarRow.RemoveAt(SimilarRow.Count - 1);
        }

        while (SimilarRow.Count < count)
        {
            SimilarRow.Add(Similar[SimilarRow.Count]);
        }
    }

    private void Fill(ObservableCollection<AlbumItem> target, IEnumerable<Album> albums, double coverWidth)
    {
        target.Clear();
        foreach (var album in albums)
        {
            target.Add(new AlbumItem(album, ImageRequest.Primary(album.Id, album.PrimaryImageTag, coverWidth, DisplayScale)));
        }
    }
}
