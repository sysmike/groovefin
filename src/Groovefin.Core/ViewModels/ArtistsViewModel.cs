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

    /// <summary>Gets the text Narrator reads for the item: the name, with the plays on the statistics page.</summary>
    public string AccessibleName => PlaysText.Length > 0 ? $"{Artist.Name}, {PlaysText}" : Artist.Name;

    /// <summary>Gets a value indicating whether the artist is downloaded with every song (#337).</summary>
    public bool IsDownloaded { get; init; }

    /// <summary>Gets how often the artist's songs were played, e.g. "48 plays", on the statistics page (#382); empty elsewhere.</summary>
    public string PlaysText { get; init; } = string.Empty;
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
    [NotifyPropertyChangedFor(nameof(CanJumpToLetter))]
    public partial ArtistSortField SortBy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJumpToLetter))]
    public partial bool Descending { get; set; }

    /// <summary>Gets a value indicating whether the list can jump to a letter: sorted by name, A to Z (#390).</summary>
    public bool CanJumpToLetter => SortBy == ArtistSortField.Name && !Descending;

    /// <summary>Raised with the index of the item a letter jump arrived at, for the view to show and focus it.</summary>
    public event EventHandler<int>? JumpRequested;

    [ObservableProperty]
    public partial bool FavouritesOnly { get; set; }

    /// <summary>Gets or sets a value indicating whether only artists with downloaded songs are shown (#400).</summary>
    [ObservableProperty]
    public partial bool DownloadedOnly { get; set; }

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

    /// <summary>Loads the artists up to the first one with <paramref name="letter"/> and asks the view to show it (#390).</summary>
    [RelayCommand]
    private async Task JumpToLetterAsync(string letter)
    {
        var generation = _generation;
        try
        {
            var index = await LetterJump.LoadUpToAsync(
                letter,
                async name => (await _library.GetArtistsAsync(CurrentQuery() with { StartIndex = 0, Limit = 1, NameLessThan = name }, _loadCancellation.Token)).TotalCount,
                () => Artists.Count,
                () => TotalCount,
                count => LoadPageAsync(generation, count));
            if (index is { } found && generation == _generation)
            {
                JumpRequested?.Invoke(this, found);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            _errors.Report(ex, Operations.LoadArtists);
        }
        catch (OperationCanceledException)
        {
            // Reloaded or closed meanwhile.
        }
    }

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

    partial void OnDownloadedOnlyChanged(bool value) => _ = ReloadAsync();

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

    private async Task LoadPageAsync(int generation, int? limit = null)
    {
        var cancellation = _loadCancellation.Token;
        IsLoading = true;
        try
        {
            var page = await _library.GetArtistsAsync(CurrentQuery() with { Limit = Math.Max(PageSize, limit ?? 0) }, cancellation);

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
        DownloadedOnly = DownloadedOnly,
    };
}
