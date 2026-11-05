using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>The album lists on the home page, in display order.</summary>
public enum HomeShelfKind
{
    RecentlyPlayed,
    RecentlyAdded,
}

/// <summary>One horizontal album list on the home page with its own loading, empty and error state.</summary>
public sealed partial class HomeShelf(HomeShelfKind kind, string title) : ObservableObject
{
    public HomeShelfKind Kind { get; } = kind;

    public string Title { get; } = title;

    public ObservableCollection<AlbumItem> Albums { get; } = [];

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
/// Home page: a random album, then the recently played and recently added albums. The lists and the
/// album load in parallel; a failing list shows its error state and does not stop the others.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IDisposable
{
    /// <summary>Number of albums per list.</summary>
    public const int ShelfSize = 20;

    /// <summary>Tile width in device-independent pixels; covers are requested for this width.</summary>
    public const double TileWidth = 152;

    /// <summary>Cover width of the random album in device-independent pixels.</summary>
    public const double HighlightCoverWidth = 200;

    private readonly ILibraryService _library;
    private readonly INavigationService _navigation;
    private readonly IErrorReporter _errors;
    private readonly IStringResources _strings;
    private readonly IPlayer _player;
    private readonly FavouritesService _favourites;
    private readonly ICoverColours _colours;
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
        ICoverColours colours)
    {
        _library = library;
        _navigation = navigation;
        _errors = errors;
        _strings = strings;
        _player = player;
        _favourites = favourites;
        _colours = colours;
        Shelves = Enum.GetValues<HomeShelfKind>()
            .Select(kind => new HomeShelf(kind, strings.GetString($"HomeShelf_{kind}")))
            .ToList();
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

    /// <summary>Gets the query for a list.</summary>
    public static AlbumQuery QueryFor(HomeShelfKind kind) => kind switch
    {
        HomeShelfKind.RecentlyPlayed => new() { Limit = ShelfSize, SortBy = AlbumSortField.DatePlayed, Descending = true, PlayedOnly = true },
        HomeShelfKind.RecentlyAdded => new() { Limit = ShelfSize, SortBy = AlbumSortField.DateAdded, Descending = true },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

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
            var page = await _library.GetAlbumsAsync(QueryFor(shelf.Kind), cancellation);
            if (cancellation.IsCancellationRequested)
            {
                return null;
            }

            shelf.Albums.Clear();
            foreach (var album in page.Items)
            {
                shelf.Albums.Add(new AlbumItem(album, ImageRequest.Primary(album.Id, album.PrimaryImageTag, TileWidth, DisplayScale)));
            }

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
}
