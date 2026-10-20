using System.Collections.ObjectModel;
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

/// <summary>One artist tile or row.</summary>
public sealed record ArtistItem(Artist Artist, ImageRequest? Image)
{
    public string Name => Artist.Name;

    public string AccessibleName => Artist.Name;
}

/// <summary>
/// Artists page: album artists or all artists, paged, with sorting and a favourites filter. Scope, sort
/// and view mode are remembered in settings.
/// </summary>
public sealed partial class ArtistsViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 100;

    /// <summary>Tile width in device-independent pixels; images are requested for this width.</summary>
    public const double TileWidth = 160;

    private readonly ILibraryService _library;
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly IErrorReporter _errors;
    private CancellationTokenSource _loadCancellation = new();
    private bool _disposed;
    private int _generation;
    private bool _initialized;

    public ArtistsViewModel(ILibraryService library, ISettingsService settings, INavigationService navigation, IErrorReporter errors)
    {
        _library = library;
        _settings = settings;
        _navigation = navigation;
        _errors = errors;

        var browse = settings.Current.Browse;
        ViewMode = browse.ArtistView;
        SortBy = browse.ArtistSort;
        Descending = browse.ArtistSortDescending;
        AlbumArtistsOnly = browse.AlbumArtistsOnly;
    }

    public ObservableCollection<ArtistItem> Artists { get; } = [];

    public IReadOnlyList<ArtistSortField> SortFields { get; } = Enum.GetValues<ArtistSortField>();

    /// <summary>Gets or sets the DPI scale of the view, used to request images in physical pixels.</summary>
    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial int TotalCount { get; private set; }

    /// <summary>Gets a value indicating whether loading finished and nothing matched.</summary>
    public bool IsEmpty => _initialized && !IsLoading && Artists.Count == 0;

    [ObservableProperty]
    public partial CollectionViewMode ViewMode { get; set; }

    [ObservableProperty]
    public partial ArtistSortField SortBy { get; set; }

    [ObservableProperty]
    public partial bool Descending { get; set; }

    [ObservableProperty]
    public partial bool FavouritesOnly { get; set; }

    /// <summary>Gets or sets a value indicating whether only album artists are listed.</summary>
    [ObservableProperty]
    public partial bool AlbumArtistsOnly { get; set; }

    /// <summary>Loads the first page. Called once when the page is shown.</summary>
    public Task InitializeAsync()
    {
        _initialized = true;
        return ReloadAsync();
    }

    private bool CanLoadMore() => !IsLoading && Artists.Count < TotalCount;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync() => LoadPageAsync(_generation);

    [RelayCommand]
    private void OpenArtist(ArtistItem item) => _navigation.NavigateTo(PageKeys.Artist, item.Artist.Id);

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
        _settings.Update(s => s with { Browse = s.Browse with { ArtistView = value } });

    partial void OnSortByChanged(ArtistSortField value)
    {
        _settings.Update(s => s with { Browse = s.Browse with { ArtistSort = value } });
        _ = ReloadAsync();
    }

    partial void OnDescendingChanged(bool value)
    {
        _settings.Update(s => s with { Browse = s.Browse with { ArtistSortDescending = value } });
        _ = ReloadAsync();
    }

    partial void OnAlbumArtistsOnlyChanged(bool value)
    {
        _settings.Update(s => s with { Browse = s.Browse with { AlbumArtistsOnly = value } });
        _ = ReloadAsync();
    }

    partial void OnFavouritesOnlyChanged(bool value) => _ = ReloadAsync();

    /// <summary>Starts over: cancels a running load, clears the list and loads the first page.</summary>
    private Task ReloadAsync()
    {
        if (!_initialized)
        {
            return Task.CompletedTask;
        }

        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var generation = ++_generation;

        Artists.Clear();
        TotalCount = 0;
        IsLoading = false;
        return LoadPageAsync(generation);
    }

    private async Task LoadPageAsync(int generation)
    {
        var cancellation = _loadCancellation.Token;
        IsLoading = true;
        try
        {
            var page = await _library.GetArtistsAsync(CurrentQuery(), cancellation);

            // A newer reload started while this page was loading: drop the stale result.
            if (generation != _generation)
            {
                return;
            }

            foreach (var artist in page.Items)
            {
                Artists.Add(new ArtistItem(artist, ImageRequest.Primary(artist.Id, artist.PrimaryImageTag, TileWidth, DisplayScale)));
            }

            TotalCount = page.TotalCount;
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            if (generation == _generation)
            {
                _errors.Report(ex, Operations.LoadArtists);
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

    private ArtistQuery CurrentQuery() => new()
    {
        StartIndex = Artists.Count,
        Limit = PageSize,
        AlbumArtistsOnly = AlbumArtistsOnly,
        SortBy = SortBy,
        Descending = Descending,
        FavouritesOnly = FavouritesOnly,
    };
}
