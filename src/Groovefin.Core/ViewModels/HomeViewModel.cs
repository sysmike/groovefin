using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Images;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>The album lists on the home page, in display order.</summary>
public enum HomeShelfKind
{
    RecentlyAdded,
    RecentlyPlayed,
    MostPlayed,
    Random,
    Favourites,
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
/// Home page: recently added, recently played, most played, random and favourite albums. The lists load
/// in parallel; a failing list shows its error state and does not stop the others.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IDisposable
{
    /// <summary>Number of albums per list.</summary>
    public const int ShelfSize = 20;

    /// <summary>Tile width in device-independent pixels; covers are requested for this width.</summary>
    public const double TileWidth = 152;

    private readonly ILibraryService _library;
    private readonly INavigationService _navigation;
    private readonly IErrorReporter _errors;
    private CancellationTokenSource _cancellation = new();

    public HomeViewModel(ILibraryService library, INavigationService navigation, IErrorReporter errors, IStringResources strings)
    {
        _library = library;
        _navigation = navigation;
        _errors = errors;
        Shelves = Enum.GetValues<HomeShelfKind>()
            .Select(kind => new HomeShelf(kind, strings.GetString($"HomeShelf_{kind}")))
            .ToList();
    }

    public IReadOnlyList<HomeShelf> Shelves { get; }

    /// <summary>Gets a value indicating whether a refresh is running.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Gets or sets the DPI scale of the view, used to request covers in physical pixels.</summary>
    public double DisplayScale { get; set; } = 1;

    /// <summary>Gets the query for a list.</summary>
    public static AlbumQuery QueryFor(HomeShelfKind kind) => kind switch
    {
        HomeShelfKind.RecentlyAdded => new() { Limit = ShelfSize, SortBy = AlbumSortField.DateAdded, Descending = true },
        HomeShelfKind.RecentlyPlayed => new() { Limit = ShelfSize, SortBy = AlbumSortField.DatePlayed, Descending = true, PlayedOnly = true },
        HomeShelfKind.MostPlayed => new() { Limit = ShelfSize, SortBy = AlbumSortField.PlayCount, Descending = true, PlayedOnly = true },
        HomeShelfKind.Random => new() { Limit = ShelfSize, SortBy = AlbumSortField.Random },
        HomeShelfKind.Favourites => new() { Limit = ShelfSize, SortBy = AlbumSortField.Random, FavouritesOnly = true },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Loads all lists again. A load that is still running is cancelled.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
        var cancellation = _cancellation.Token;

        IsLoading = true;
        var failures = await Task.WhenAll(Shelves.Select(shelf => LoadShelfAsync(shelf, cancellation)));
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

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
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
