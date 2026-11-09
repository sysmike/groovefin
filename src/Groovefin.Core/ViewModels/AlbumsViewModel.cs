using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>One album tile or row.</summary>
public sealed record AlbumItem(Album Album, ImageRequest? Cover)
{
    public string Name => Album.Name;

    public string Artist => Album.AlbumArtist ?? string.Empty;

    public string Year => Album.Year?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>Gets the text Narrator reads for the item, e.g. "Discovery, Daft Punk, 2001", with the plays on the statistics page.</summary>
    public string AccessibleName => string.Join(", ", new[] { Name, Artist, Year, PlaysText }.Where(s => s.Length > 0));

    /// <summary>Gets a value indicating whether the album is downloaded with every song (#337).</summary>
    public bool IsDownloaded { get; init; }

    /// <summary>Gets how often the album was played, e.g. "21 plays", on the statistics page (#382); empty elsewhere.</summary>
    public string PlaysText { get; init; } = string.Empty;
}

/// <summary>The order to open the Albums page in, e.g. from "Show all" on the home page (#376).</summary>
public sealed record AlbumsOrder(AlbumSortField SortBy, bool Descending);

/// <summary>
/// Albums page: pages through all albums of the active server with sorting and filters. Loads the next
/// page when the view scrolls near the end (<see cref="LoadMoreCommand"/>). Sort and view mode are
/// remembered in settings.
/// </summary>
public sealed partial class AlbumsViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 100;

    /// <summary>Tile width in device-independent pixels; covers are requested for this width.</summary>
    public const double TileWidth = 168;

    private readonly ILibraryService _library;
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly IErrorReporter _errors;
    private CancellationTokenSource _loadCancellation = new();
    private bool _disposed;
    private int _generation;
    private bool _initialized;
    private bool _holdReload;

    public AlbumsViewModel(ILibraryService library, ISettingsService settings, INavigationService navigation, IErrorReporter errors)
    {
        _library = library;
        _settings = settings;
        _navigation = navigation;
        _errors = errors;

        var browse = settings.Current.Browse;
        ViewMode = browse.AlbumView;
        SortBy = browse.AlbumSort;
        Descending = browse.AlbumSortDescending;
    }

    public ObservableCollection<AlbumItem> Albums { get; } = [];

    public IReadOnlyList<AlbumSortField> SortFields { get; } = Enum.GetValues<AlbumSortField>();

    /// <summary>Gets the genres to filter by. The first entry (<see langword="null"/>) means all genres.</summary>
    public ObservableCollection<Genre?> Genres { get; } = [null];

    /// <summary>Gets or sets the DPI scale of the view, used to request covers in physical pixels.</summary>
    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial int TotalCount { get; private set; }

    /// <summary>Gets a value indicating whether loading finished and nothing matched.</summary>
    public bool IsEmpty => _initialized && !IsLoading && Albums.Count == 0;

    [ObservableProperty]
    public partial CollectionViewMode ViewMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJumpToLetter))]
    public partial AlbumSortField SortBy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJumpToLetter))]
    public partial bool Descending { get; set; }

    /// <summary>Gets a value indicating whether the list can jump to a letter: sorted by name, A to Z (#390).</summary>
    public bool CanJumpToLetter => SortBy == AlbumSortField.Name && !Descending;

    /// <summary>Raised with the index of the item a letter jump arrived at, for the view to show and focus it.</summary>
    public event EventHandler<int>? JumpRequested;

    [ObservableProperty]
    public partial bool FavouritesOnly { get; set; }

    [ObservableProperty]
    public partial Genre? Genre { get; set; }

    [ObservableProperty]
    public partial int? MinYear { get; set; }

    [ObservableProperty]
    public partial int? MaxYear { get; set; }

    /// <summary>Gets a value indicating whether any filter is active (for the filter button state).</summary>
    public bool HasFilters => FavouritesOnly || Genre is not null || MinYear is not null || MaxYear is not null;

    /// <summary>Loads genres and the first page. Called once when the page is shown.</summary>
    public async Task InitializeAsync()
    {
        _initialized = true;
        var reload = ReloadAsync();
        try
        {
            foreach (var genre in await _library.GetAlbumGenresAsync(_loadCancellation.Token))
            {
                Genres.Add(genre);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            // The album list still works without the genre filter.
            _errors.Report(ex, Operations.LoadAlbums);
        }

        await reload;
    }

    /// <summary>
    /// Shows every album in <paramref name="order"/>: filters are cleared and the list reloads once. Before
    /// <see cref="InitializeAsync"/> it only sets the order, which the first load uses.
    /// </summary>
    public Task ShowAsync(AlbumsOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _holdReload = true;
        try
        {
            SortBy = order.SortBy;
            Descending = order.Descending;
            ClearFilters();
        }
        finally
        {
            _holdReload = false;
        }

        return ReloadAsync();
    }

    private bool CanLoadMore() => !IsLoading && Albums.Count < TotalCount;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync() => LoadPageAsync(_generation);

    /// <summary>Loads the albums up to the first one with <paramref name="letter"/> and asks the view to show it (#390).</summary>
    [RelayCommand]
    private async Task JumpToLetterAsync(string letter)
    {
        var generation = _generation;
        try
        {
            var index = await LetterJump.LoadUpToAsync(
                letter,
                async name => (await _library.GetAlbumsAsync(CurrentQuery() with { StartIndex = 0, Limit = 1, NameLessThan = name }, _loadCancellation.Token)).TotalCount,
                () => Albums.Count,
                () => TotalCount,
                count => LoadPageAsync(generation, count));
            if (index is { } found && generation == _generation)
            {
                JumpRequested?.Invoke(this, found);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            _errors.Report(ex, Operations.LoadAlbums);
        }
        catch (OperationCanceledException)
        {
            // Reloaded or closed meanwhile.
        }
    }

    [RelayCommand]
    private void OpenAlbum(AlbumItem item) => _navigation.NavigateTo(PageKeys.Album, item.Album.Id);

    [RelayCommand]
    private void ClearFilters()
    {
        FavouritesOnly = false;
        Genre = null;
        MinYear = null;
        MaxYear = null;
    }

    /// <summary>Cancels loading. Safe to call twice: the page and, at shutdown, the container dispose it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
    }

    partial void OnViewModeChanged(CollectionViewMode value) =>
        _settings.Update(s => s with { Browse = s.Browse with { AlbumView = value } });

    partial void OnSortByChanged(AlbumSortField value)
    {
        _settings.Update(s => s with { Browse = s.Browse with { AlbumSort = value } });
        _ = ReloadAsync();
    }

    partial void OnDescendingChanged(bool value)
    {
        _settings.Update(s => s with { Browse = s.Browse with { AlbumSortDescending = value } });
        _ = ReloadAsync();
    }

    partial void OnFavouritesOnlyChanged(bool value) => FilterChanged();

    partial void OnGenreChanged(Genre? value) => FilterChanged();

    partial void OnMinYearChanged(int? value) => FilterChanged();

    partial void OnMaxYearChanged(int? value) => FilterChanged();

    private void FilterChanged()
    {
        OnPropertyChanged(nameof(HasFilters));
        _ = ReloadAsync();
    }

    /// <summary>Starts over: cancels a running load, clears the list and loads the first page.</summary>
    public Task ReloadAsync()
    {
        if (!_initialized || _holdReload)
        {
            return Task.CompletedTask;
        }

        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var generation = ++_generation;

        Albums.Clear();
        TotalCount = 0;
        IsLoading = false;
        return LoadPageAsync(generation);
    }

    private async Task LoadPageAsync(int generation, int? limit = null)
    {
        var cancellation = _loadCancellation.Token;
        IsLoading = true;
        try
        {
            var page = await _library.GetAlbumsAsync(CurrentQuery() with { Limit = Math.Max(PageSize, limit ?? 0) }, cancellation);

            // A newer reload started while this page was loading: drop the stale result.
            if (generation != _generation)
            {
                return;
            }

            foreach (var album in page.Items)
            {
                Albums.Add(new AlbumItem(album, ImageRequest.Primary(album.Id, album.PrimaryImageTag, TileWidth, DisplayScale)));
            }

            TotalCount = page.TotalCount;
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            if (generation == _generation)
            {
                _errors.Report(ex, Operations.LoadAlbums);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a reload or the page was closed.
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    private AlbumQuery CurrentQuery() => new()
    {
        StartIndex = Albums.Count,
        Limit = PageSize,
        SortBy = SortBy,
        Descending = Descending,
        GenreIds = Genre is null ? [] : [Genre.Id],
        MinYear = MinYear,
        MaxYear = MaxYear,
        FavouritesOnly = FavouritesOnly,
    };
}
