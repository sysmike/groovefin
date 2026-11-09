using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.History;
using Groovefin.Core.Images;
using Groovefin.Core.Layout;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// The album rows of the home page, in their default order (#394). Settings shows and hides them and changes the
/// order (<see cref="HomeLayout"/>).
/// </summary>
public enum HomeShelfKind
{
    RecentlyPlayed,
    RecentlyAdded,

    /// <summary>The albums whose songs were played most often, together.</summary>
    MostPlayed,

    /// <summary>The favourite albums, in random order.</summary>
    FavouriteAlbums,

    /// <summary>Albums played before, but none of their songs in the last year (the server's play dates).</summary>
    NotPlayedInAYear,

    /// <summary>The albums of an artist from the play history, or of any artist without one.</summary>
    RandomArtist,

    /// <summary>The albums played most in Groovefin in the week around this day a year ago (the play history).</summary>
    ThisWeekLastYear,
}

/// <summary>
/// One row of albums on the home page with its own loading, empty and error state. It shows only as
/// many of its albums as fit in the window (#376); "Show all" opens the Albums page in the same order,
/// or the row's artist (#394).
/// </summary>
public sealed partial class HomeShelf(HomeShelfKind kind, string title, string showAllName, string emptyText) : ObservableObject
{
    private List<AlbumItem> _loaded = [];
    private int _tiles = int.MaxValue;

    public HomeShelfKind Kind { get; } = kind;

    /// <summary>Gets the row's title; a random artist's row gets the artist's name once loaded.</summary>
    [ObservableProperty]
    public partial string Title { get; internal set; } = title;

    /// <summary>Gets the "Show all" link's name, e.g. "Show all: Recently played".</summary>
    [ObservableProperty]
    public partial string ShowAllName { get; internal set; } = showAllName;

    /// <summary>Gets what the row says without albums, e.g. how it fills.</summary>
    public string EmptyText { get; } = emptyText;

    /// <summary>Gets the artist of a random artist's row, for Show all.</summary>
    internal string? ArtistId { get; set; }

    /// <summary>Gets the albums of a row that is a set of its own (this week last year), for Show all.</summary>
    internal IReadOnlyList<string> AlbumIds { get; set; } = [];

    /// <summary>Gets the albums shown: the first of the loaded ones, as many as fit.</summary>
    public ObservableCollection<AlbumItem> Albums { get; } = [];

    /// <summary>Gets a value indicating whether the row has albums, so "Show all" has something to show.</summary>
    [ObservableProperty]
    public partial bool HasAlbums { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ShowsProgress))]
    public partial bool IsLoading { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool LoadFailed { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ShowsProgress))]
    public partial bool IsLoaded { get; internal set; }

    /// <summary>Gets a value indicating whether the list loaded and has no albums.</summary>
    public bool IsEmpty => IsLoaded && !IsLoading && !LoadFailed && Albums.Count == 0;

    /// <summary>Gets a value indicating whether the first load is running (a refresh keeps the old albums).</summary>
    public bool ShowsProgress => IsLoading && !IsLoaded;

    /// <summary>
    /// Gets how many tiles fit side by side in a row this wide (without its padding): the columns of a tile
    /// grid, whose tiles then grow to fill the row. At least one.
    /// </summary>
    public static int TilesThatFit(double width) => TileGrid.Columns(width, HomeViewModel.TileWidth, HomeViewModel.TileSpacing);

    /// <summary>Shows as many albums as <paramref name="tiles"/>, or all when fewer are loaded.</summary>
    public void Fit(int tiles)
    {
        _tiles = Math.Max(1, tiles);
        Show();
    }

    /// <summary>Puts in the albums of a load; the row shows the first of them.</summary>
    internal void Replace(IEnumerable<AlbumItem> albums)
    {
        _loaded = [.. albums];
        Albums.Clear();
        Show();
        HasAlbums = _loaded.Count > 0;
    }

    /// <summary>Adds or removes albums at the end only, so a resize does not rebuild the row.</summary>
    private void Show()
    {
        var count = Math.Min(_tiles, _loaded.Count);
        while (Albums.Count > count)
        {
            Albums.RemoveAt(Albums.Count - 1);
        }

        while (Albums.Count < count)
        {
            Albums.Add(_loaded[Albums.Count]);
        }
    }
}

/// <summary>
/// The random album at the top of the home page (#367): its cover, details and songs, and the colour
/// taken from its cover for the card behind it.
/// </summary>
public sealed partial class HomeHighlight(Album album, ImageRequest? cover, IReadOnlyList<Track> tracks, string details) : ObservableObject
{
    public Album Album { get; } = album;

    public ImageRequest? Cover { get; } = cover;

    /// <summary>Gets the album's songs in disc and track order, which play and shuffle use.</summary>
    public IReadOnlyList<Track> Tracks { get; } = tracks;

    public string Name => Album.Name;

    public string Artist => Album.AlbumArtist ?? string.Empty;

    /// <summary>Gets the year, song count and length, as on the album page.</summary>
    public string Details { get; } = details;

    public IReadOnlyList<Genre> Genres => Album.Genres;

    public bool HasGenres => Album.Genres.Count > 0;

    public bool HasTracks => Tracks.Count > 0;

    /// <summary>Gets the play button's name, e.g. "Play Discovery".</summary>
    public string PlayName { get; init; } = string.Empty;

    /// <summary>Gets the shuffle button's name, e.g. "Shuffle Discovery".</summary>
    public string ShuffleName { get; init; } = string.Empty;

    /// <summary>Gets the cover's colour, darkened for white text; <see langword="null"/> without a cover.</summary>
    public CoverColour? Colour { get; init; }

    [ObservableProperty]
    public partial bool IsFavourite { get; internal set; }
}

/// <summary>
/// Home page: a random album, then the rows chosen in Settings (#394), by default the recently played and recently
/// added albums. The lists and the album load in parallel; a failing list shows its error state and does not stop
/// the others.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IDisposable
{
    /// <summary>Number of albums per list.</summary>
    public const int ShelfSize = 20;

    /// <summary>Least tile width in device-independent pixels; covers are requested for this width.</summary>
    public const double TileWidth = 152;

    /// <summary>Space between two tiles in device-independent pixels.</summary>
    public const double TileSpacing = 16;

    /// <summary>Cover width of the random album in device-independent pixels.</summary>
    public const double HighlightCoverWidth = 200;

    /// <summary>The artists from the play history a random artist's row tries, for one with albums of their own.</summary>
    public const int ArtistTries = 3;

    /// <summary>The played songs of last year's week whose albums are looked up on the server, at most.</summary>
    public const int MaxLookups = 200;

    /// <summary>How far a year ago "this week" reaches before and after the day.</summary>
    public static readonly TimeSpan HalfWeek = TimeSpan.FromDays(3.5);

    private readonly ILibraryService _library;
    private readonly INavigationService _navigation;
    private readonly IErrorReporter _errors;
    private readonly IStringResources _strings;
    private readonly IPlayer _player;
    private readonly FavouritesService _favourites;
    private readonly ICoverColours _colours;
    private readonly IPlayLog _history;
    private readonly TimeProvider _time;
    private CancellationTokenSource _cancellation = new();
    private CancellationTokenSource _highlightCancellation = new();
    private bool _disposed;

    public HomeViewModel(
        ILibraryService library,
        INavigationService navigation,
        IErrorReporter errors,
        IStringResources strings,
        IPlayer player,
        FavouritesService favourites,
        ICoverColours colours,
        ISettingsService settings,
        IPlayLog history,
        TimeProvider time)
    {
        _library = library;
        _navigation = navigation;
        _errors = errors;
        _strings = strings;
        _player = player;
        _favourites = favourites;
        _colours = colours;
        _history = history;
        _time = time;
        Shelves = [.. HomeLayout.VisibleRows(settings.Current.Appearance.HomeRows).Select(kind => CreateShelf(kind, strings))];
    }

    public IReadOnlyList<HomeShelf> Shelves { get; }

    /// <summary>Gets a value indicating whether a refresh is running.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Gets the random album; <see langword="null"/> until it has loaded, or when the library has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHighlight))]
    [NotifyCanExecuteChangedFor(nameof(PlayHighlightCommand), nameof(ShuffleHighlightCommand), nameof(ToggleHighlightFavouriteCommand), nameof(OpenHighlightCommand))]
    public partial HomeHighlight? Highlight { get; private set; }

    public bool HasHighlight => Highlight is not null;

    /// <summary>Gets or sets the DPI scale of the view, used to request covers in physical pixels.</summary>
    public double DisplayScale { get; set; } = 1;

    /// <summary>Gets the query for a list the server answers on its own, at <paramref name="now"/>.</summary>
    public static AlbumQuery QueryFor(HomeShelfKind kind, DateTimeOffset now) => kind switch
    {
        HomeShelfKind.RecentlyPlayed => new() { Limit = ShelfSize, SortBy = AlbumSortField.DatePlayed, Descending = true, PlayedOnly = true },
        HomeShelfKind.RecentlyAdded => new() { Limit = ShelfSize, SortBy = AlbumSortField.DateAdded, Descending = true },
        HomeShelfKind.MostPlayed => new() { Limit = ShelfSize, SortBy = AlbumSortField.PlayCount, Descending = true, PlayedOnly = true },
        HomeShelfKind.FavouriteAlbums => new() { Limit = ShelfSize, SortBy = AlbumSortField.Random, FavouritesOnly = true },
        HomeShelfKind.NotPlayedInAYear => new() { Limit = ShelfSize, SortBy = AlbumSortField.DatePlayed, Descending = true, PlayedBefore = YearAgo(now) },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Gets a year before the start of <paramref name="now"/>'s day (UTC): the same all day, so the server's order can be kept between pages.</summary>
    public static DateTimeOffset YearAgo(DateTimeOffset now) => new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddYears(-1);

    /// <summary>Picks a number below the given one: random; tests replace it.</summary>
    internal Func<int, int> Pick { get; init; } = Random.Shared.Next;

    /// <summary>Gets two random albums: the second is shown when the first is the one shown already.</summary>
    public static AlbumQuery HighlightQuery { get; } = new() { Limit = 2, SortBy = AlbumSortField.Random };

    /// <summary>Loads all lists and a new random album. A load that is still running is cancelled.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
        var cancellation = _cancellation.Token;

        IsLoading = true;
        var highlight = LoadHighlightAsync(RestartHighlight());
        var failures = await Task.WhenAll(Shelves.Select(shelf => LoadShelfAsync(shelf, cancellation)).Append(highlight));
        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        IsLoading = false;

        // One notification for the page; each failed list shows its own error state.
        if (failures.FirstOrDefault(f => f is not null) is { } failure)
        {
            _errors.Report(failure, Operations.LoadHome);
        }
    }

    [RelayCommand]
    private void OpenAlbum(AlbumItem item) => _navigation.NavigateTo(PageKeys.Album, item.Album.Id);

    private bool CanPlayHighlight() => Highlight is { HasTracks: true };

    [RelayCommand(CanExecute = nameof(HasHighlight))]
    private void OpenHighlight() => _navigation.NavigateTo(PageKeys.Album, Highlight!.Album.Id);

    [RelayCommand]
    private void OpenGenre(Genre genre) => _navigation.NavigateTo(PageKeys.Genre, genre.Id);

    /// <summary>
    /// Opens the Albums page in the row's order, with every album (#376), or with the row's albums only; a random
    /// artist's row opens the artist (#394).
    /// </summary>
    [RelayCommand]
    private void ShowAll(HomeShelf shelf)
    {
        switch (shelf.Kind)
        {
            case HomeShelfKind.RandomArtist when shelf.ArtistId is { } artist:
                _navigation.NavigateTo(PageKeys.Artist, artist);
                return;
            case HomeShelfKind.RandomArtist:
                return;
            case HomeShelfKind.ThisWeekLastYear:
                _navigation.NavigateTo(PageKeys.Albums, new AlbumsOrder(AlbumSortField.Name, false)
                {
                    Selection = new AlbumSelection(shelf.Title) { Ids = shelf.AlbumIds },
                });
                return;
            case HomeShelfKind.FavouriteAlbums:
                _navigation.NavigateTo(PageKeys.Albums, new AlbumsOrder(AlbumSortField.Name, false) { FavouritesOnly = true });
                return;
        }

        var query = QueryFor(shelf.Kind, _time.GetUtcNow());
        _navigation.NavigateTo(PageKeys.Albums, new AlbumsOrder(query.SortBy, query.Descending)
        {
            Selection = query.PlayedBefore is { } before ? new AlbumSelection(shelf.Title) { PlayedBefore = before } : null,
        });
    }

    [RelayCommand(CanExecute = nameof(CanPlayHighlight))]
    private void PlayHighlight() => _player.Play(Highlight!.Tracks);

    [RelayCommand(CanExecute = nameof(CanPlayHighlight))]
    private void ShuffleHighlight() => _player.Shuffle(Highlight!.Tracks);

    /// <summary>Toggles the random album's heart: shown at once, rolled back if the server refuses.</summary>
    [RelayCommand(CanExecute = nameof(HasHighlight))]
    private async Task ToggleHighlightFavouriteAsync()
    {
        var highlight = Highlight!;
        var target = !highlight.IsFavourite;
        highlight.IsFavourite = target;
        if (!await _favourites.SetAsync(highlight.Album.Id, target))
        {
            highlight.IsFavourite = !target;
        }
    }

    /// <summary>
    /// Shows another random album; the lists stay as they are. Another click while one loads starts
    /// over: the button stays enabled, so it keeps keyboard focus.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ShowAnotherAlbumAsync()
    {
        if (await LoadHighlightAsync(RestartHighlight()) is { } failure)
        {
            _errors.Report(failure, Operations.LoadHome);
        }
    }

    /// <summary>Cancels loading. Safe to call twice: the page and, at shutdown, the container dispose it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
        _highlightCancellation.Cancel();
        _highlightCancellation.Dispose();
    }

    private static HomeShelf CreateShelf(HomeShelfKind kind, IStringResources strings)
    {
        var title = strings.GetString($"HomeShelf_{kind}");
        return new HomeShelf(kind, title, ShowAllNameOf(title, strings), strings.GetString($"HomeShelfEmpty_{kind}"));
    }

    private static string ShowAllNameOf(string title, IStringResources strings) =>
        strings.GetString("HomeShelfShowAll").Replace("{0}", title, StringComparison.Ordinal);

    private CancellationToken RestartHighlight()
    {
        _highlightCancellation.Cancel();
        _highlightCancellation.Dispose();
        _highlightCancellation = new CancellationTokenSource();
        return _highlightCancellation.Token;
    }

    /// <summary>
    /// Loads a random album other than the one shown, with its genres and songs, and its cover's colour.
    /// The card changes only once all of it is there. Returns the failure, if any.
    /// </summary>
    private async Task<Exception?> LoadHighlightAsync(CancellationToken cancellation)
    {
        try
        {
            var page = await _library.GetAlbumsAsync(HighlightQuery, cancellation);
            var picked = page.Items.FirstOrDefault(a => a.Id != Highlight?.Album.Id) ?? page.Items.FirstOrDefault();
            if (picked is null)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    Highlight = null;
                }

                return null;
            }

            // The list has no genres; the album itself has.
            var loadAlbum = _library.GetAlbumAsync(picked.Id, cancellation);
            var loadTracks = _library.GetAlbumTracksAsync(picked.Id, cancellation);
            await Task.WhenAll(loadAlbum, loadTracks);
            var (album, tracks) = (await loadAlbum, await loadTracks);
            var cover = ImageRequest.Primary(album.Id, album.PrimaryImageTag, HighlightCoverWidth, DisplayScale);
            var colour = cover is null ? null : await _colours.GetAsync(cover, cancellation);
            if (cancellation.IsCancellationRequested)
            {
                return null;
            }

            Highlight = new HomeHighlight(album, cover, tracks, AlbumDetailViewModel.BuildDetails(album, tracks, _strings))
            {
                Colour = colour?.ForWhiteText(),
                IsFavourite = _favourites.IsFavourite(album.Id, album.IsFavorite),
                PlayName = _strings.GetString("Cover_Play").Replace("{0}", album.Name, StringComparison.Ordinal),
                ShuffleName = _strings.GetString("HomeHighlight_Shuffle").Replace("{0}", album.Name, StringComparison.Ordinal),
            };
            return null;
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            return cancellation.IsCancellationRequested ? null : ex;
        }
        catch (OperationCanceledException)
        {
            // Superseded by another album or a refresh, or the page was closed.
            return null;
        }
    }

    /// <summary>Loads one list; returns the failure, if any, so the page reports it once.</summary>
    private async Task<Exception?> LoadShelfAsync(HomeShelf shelf, CancellationToken cancellation)
    {
        shelf.IsLoading = true;
        shelf.LoadFailed = false;
        try
        {
            var albums = shelf.Kind switch
            {
                HomeShelfKind.RandomArtist => await LoadRandomArtistAsync(shelf, cancellation),
                HomeShelfKind.ThisWeekLastYear => await LoadWeekLastYearAsync(shelf, cancellation),
                _ => (await _library.GetAlbumsAsync(QueryFor(shelf.Kind, _time.GetUtcNow()), cancellation)).Items,
            };
            if (cancellation.IsCancellationRequested)
            {
                return null;
            }

            shelf.Replace(albums.Select(album => new AlbumItem(album, ImageRequest.Primary(album.Id, album.PrimaryImageTag, TileWidth, DisplayScale))));

            shelf.IsLoaded = true;
            return null;
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            if (cancellation.IsCancellationRequested)
            {
                return null;
            }

            shelf.LoadFailed = true;
            return ex;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a refresh or the page was closed.
            return null;
        }
        finally
        {
            if (!cancellation.IsCancellationRequested)
            {
                shelf.IsLoading = false;
            }
        }
    }

    /// <summary>
    /// The albums of an artist from the play history: a few picked at random, the first with albums of their own;
    /// without one, a random artist of the library. The row takes the artist's name.
    /// </summary>
    private async Task<IReadOnlyList<Album>> LoadRandomArtistAsync(HomeShelf shelf, CancellationToken cancellation)
    {
        var log = await _history.GetLogAsync();
        var played = log.Plays
            .Select(p => log.Tracks.GetValueOrDefault(p.TrackId))
            .OfType<PlayedTrack>()
            .SelectMany(t => t.ArtistIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < ArtistTries && played.Count > 0; i++)
        {
            var id = played[Pick(played.Count)];
            played.Remove(id);
            var albums = await ArtistAlbumsAsync(id, cancellation);
            if (albums.Count > 0)
            {
                var artist = await _library.GetArtistAsync(id, cancellation);
                ShowArtist(shelf, artist);
                return albums;
            }
        }

        var any = await _library.GetArtistsAsync(new ArtistQuery { SortBy = ArtistSortField.Random, Limit = 1 }, cancellation);
        if (any.Items.FirstOrDefault() is not { } random)
        {
            return [];
        }

        ShowArtist(shelf, random);
        return await ArtistAlbumsAsync(random.Id, cancellation);
    }

    private async Task<IReadOnlyList<Album>> ArtistAlbumsAsync(string artistId, CancellationToken cancellation) =>
        (await _library.GetAlbumsAsync(new AlbumQuery { AlbumArtistId = artistId, Limit = ShelfSize, SortBy = AlbumSortField.Year, Descending = true }, cancellation)).Items;

    private void ShowArtist(HomeShelf shelf, Artist artist)
    {
        shelf.ArtistId = artist.Id;
        shelf.Title = _strings.GetString("HomeShelf_MoreBy").Replace("{0}", artist.Name, StringComparison.Ordinal);
        shelf.ShowAllName = ShowAllNameOf(shelf.Title, _strings);
    }

    /// <summary>
    /// The albums played most in Groovefin in the week around this day a year ago: played in Groovefin or on other
    /// devices, not the first sync's guesses. Songs the history has no album for are looked up on the server.
    /// </summary>
    private async Task<IReadOnlyList<Album>> LoadWeekLastYearAsync(HomeShelf shelf, CancellationToken cancellation)
    {
        var log = await _history.GetLogAsync();
        var day = _time.GetUtcNow().AddYears(-1);
        var plays = log.Plays
            .Where(p => p.Source != PlaySource.Seeded && p.PlayedAt >= day - HalfWeek && p.PlayedAt < day + HalfWeek)
            .Select(p => p.TrackId)
            .ToList();
        var albumOf = plays.Distinct(StringComparer.Ordinal)
            .Where(log.Tracks.ContainsKey)
            .ToDictionary(id => id, id => log.Tracks[id].AlbumId, StringComparer.Ordinal);
        var missing = plays.Where(id => !albumOf.ContainsKey(id)).Distinct(StringComparer.Ordinal).Take(MaxLookups).ToList();
        if (missing.Count > 0)
        {
            foreach (var track in await _library.GetTracksByIdAsync(missing, cancellation))
            {
                albumOf[track.Id] = track.AlbumId;
            }
        }

        List<string> ranked =
        [
            .. plays.Select(id => albumOf.GetValueOrDefault(id)).OfType<string>()
                .GroupBy(id => id, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(ShelfSize),
        ];
        shelf.AlbumIds = ranked;
        if (ranked.Count == 0)
        {
            return [];
        }

        var found = (await _library.GetAlbumsAsync(new AlbumQuery { Ids = ranked, Limit = ranked.Count }, cancellation)).Items
            .DistinctBy(a => a.Id)
            .ToDictionary(a => a.Id, StringComparer.Ordinal);
        return [.. ranked.Where(found.ContainsKey).Select(id => found[id])];
    }
}
