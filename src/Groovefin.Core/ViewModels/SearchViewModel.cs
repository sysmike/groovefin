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
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>A track in the search results.</summary>
public sealed partial class SearchTrackItem(Track track, int index) : ObservableObject, ICurrentTrackRow
{
    public Track Track { get; } = track;

    public int Index { get; } = index;

    /// <summary>Gets or sets whether the row's track is playing (#247).</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public string Name => Track.Name;

    /// <summary>Gets the artists and the album, e.g. "Daft Punk · Discovery".</summary>
    public string Details => string.Join(" · ", new[] { string.Join(", ", Track.Artists.Select(a => a.Name)), Track.AlbumName ?? string.Empty }.Where(s => s.Length > 0));

    public string Duration => Durations.Track(Track.Duration);

    public string AccessibleName => string.Join(", ", new[] { Name, Details, Duration }.Where(s => s.Length > 0));
}

/// <summary>
/// Search results page: matches grouped by artists, albums, tracks and playlists. The summary line is a
/// live region, so Narrator announces how many results there are.
/// </summary>
public sealed partial class SearchViewModel(
    ISearchService search,
    IPlayer player,
    INavigationService navigation,
    IErrorReporter errors,
    IStringResources strings,
    CurrentTrack currentTrack) : ObservableObject
{
    /// <summary>Most results shown per kind.</summary>
    public const int Limit = 50;

    public const double TileWidth = 152;

    public const double PlaylistImageWidth = 48;

    public ObservableCollection<ArtistItem> Artists { get; } = [];

    public ObservableCollection<AlbumItem> Albums { get; } = [];

    public ObservableCollection<SearchTrackItem> Tracks { get; } = currentTrack.Watch(new ObservableCollection<SearchTrackItem>());

    public ObservableCollection<PlaylistItem> Playlists { get; } = [];

    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    public partial string Query { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    /// <summary>Gets the result count line, e.g. "12 results for “disco”"; empty while loading.</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasArtists { get; private set; }

    [ObservableProperty]
    public partial bool HasAlbums { get; private set; }

    [ObservableProperty]
    public partial bool HasTracks { get; private set; }

    [ObservableProperty]
    public partial bool HasPlaylists { get; private set; }

    public async Task LoadAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Query = query.Trim();
        IsLoading = true;
        LoadFailed = false;
        Summary = string.Empty;
        try
        {
            var results = await search.SearchAsync(Query, Limit, cancellationToken);
            Show(results);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            LoadFailed = true;
            Show(SearchResults.Empty);
            Summary = string.Empty;
            errors.Report(ex, Operations.Search);
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

    [RelayCommand]
    private void OpenArtist(ArtistItem item) => navigation.NavigateTo(PageKeys.Artist, item.Artist.Id);

    [RelayCommand]
    private void OpenAlbum(AlbumItem item) => navigation.NavigateTo(PageKeys.Album, item.Album.Id);

    [RelayCommand]
    private void OpenPlaylist(PlaylistItem item) => navigation.NavigateTo(PageKeys.Playlist, item.Playlist.Id);

    /// <summary>Plays the found tracks from <paramref name="item"/> on.</summary>
    [RelayCommand]
    private void PlayTrack(SearchTrackItem item) => player.Play([.. Tracks.Select(t => t.Track)], item.Index);

    private void Show(SearchResults results)
    {
        Artists.Clear();
        foreach (var artist in results.Artists)
        {
            Artists.Add(new ArtistItem(artist, ImageRequest.Primary(artist.Id, artist.PrimaryImageTag, TileWidth, DisplayScale)));
        }

        Albums.Clear();
        foreach (var album in results.Albums)
        {
            Albums.Add(new AlbumItem(album, ImageRequest.Primary(album.Id, album.PrimaryImageTag, TileWidth, DisplayScale)));
        }

        Tracks.Clear();
        for (var i = 0; i < results.Tracks.Count; i++)
        {
            Tracks.Add(new SearchTrackItem(results.Tracks[i], i));
        }

        Playlists.Clear();
        foreach (var playlist in results.Playlists)
        {
            Playlists.Add(PlaylistItem.Create(playlist, PlaylistImageWidth, DisplayScale, strings));
        }

        HasArtists = Artists.Count > 0;
        HasAlbums = Albums.Count > 0;
        HasTracks = Tracks.Count > 0;
        HasPlaylists = Playlists.Count > 0;
        Summary = results.Count == 0
            ? strings.GetString("Search_NoResults").Replace("{0}", Query, StringComparison.Ordinal)
            : strings.GetString(results.Count == 1 ? "Search_OneResult" : "Search_Results")
                .Replace("{0}", results.Count.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{1}", Query, StringComparison.Ordinal);
    }
}
