using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Formatting;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>A favourite song: cover, title, artists · album, length.</summary>
public sealed record FavouriteTrackItem(Track Track, ImageRequest? Cover)
{
    public string Name => Track.Name;

    public string Details => string.Join(" · ", new[] { string.Join(", ", Track.Artists.Select(a => a.Name)), Track.AlbumName ?? string.Empty }.Where(s => s.Length > 0));

    public string Duration => Durations.Track(Track.Duration);

    public string AccessibleName => string.Join(", ", new[] { Name, Details, Duration }.Where(s => s.Length > 0));
}

/// <summary>
/// Favourites page: the user's favourite songs, albums and artists. Removing a heart here, or anywhere
/// else, takes the item off the page at once; new favourites show on the next visit.
/// </summary>
public sealed partial class FavouritesViewModel(
    ILibraryService library,
    IPlayer player,
    INavigationService navigation,
    IErrorReporter errors,
    FavouritesService favourites,
    IMessenger messenger) : ObservableObject
{
    /// <summary>Most favourites loaded per kind.</summary>
    public const int Limit = 500;

    public const double TileWidth = 168;

    public const double CoverWidth = 40;

    public ObservableCollection<FavouriteTrackItem> Tracks { get; } = [];

    public ObservableCollection<AlbumItem> Albums { get; } = [];

    public ObservableCollection<ArtistItem> Artists { get; } = [];

    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayAllCommand), nameof(ShuffleCommand))]
    public partial bool HasTracks { get; private set; }

    [ObservableProperty]
    public partial bool HasAlbums { get; private set; }

    [ObservableProperty]
    public partial bool HasArtists { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // Weak: the page's view model needs no unregistering.
        if (!messenger.IsRegistered<FavouriteChangedMessage>(this))
        {
            messenger.Register<FavouritesViewModel, FavouriteChangedMessage>(this, static (r, m) => r.OnFavouriteChanged(m));
        }

        IsLoading = true;
        LoadFailed = false;
        try
        {
            var tracks = library.GetFavouriteTracksAsync(Limit, cancellationToken);
            var albums = library.GetAlbumsAsync(new AlbumQuery { FavouritesOnly = true, Limit = Limit }, cancellationToken);
            var artists = library.GetArtistsAsync(new ArtistQuery { FavouritesOnly = true, AlbumArtistsOnly = false, Limit = Limit }, cancellationToken);
            await Task.WhenAll(tracks, albums, artists);

            Tracks.Clear();
            foreach (var track in await tracks)
            {
                var cover = track.AlbumId is { } albumId ? ImageRequest.Primary(albumId, track.AlbumImageTag, CoverWidth, DisplayScale) : null;
                Tracks.Add(new FavouriteTrackItem(track, cover));
            }

            Albums.Clear();
            foreach (var album in (await albums).Items)
            {
                Albums.Add(new AlbumItem(album, ImageRequest.Primary(album.Id, album.PrimaryImageTag, TileWidth, DisplayScale)));
            }

            Artists.Clear();
            foreach (var artist in (await artists).Items)
            {
                Artists.Add(new ArtistItem(artist, ImageRequest.Primary(artist.Id, artist.PrimaryImageTag, TileWidth, DisplayScale)));
            }

            UpdateFlags();
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadFavourites);
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

    private IReadOnlyList<Track> AllTracks => [.. Tracks.Select(t => t.Track)];

    [RelayCommand(CanExecute = nameof(HasTracks))]
    private void PlayAll() => player.Play(AllTracks);

    [RelayCommand(CanExecute = nameof(HasTracks))]
    private void Shuffle() => player.Shuffle(AllTracks);

    /// <summary>Plays the favourite songs from <paramref name="item"/> on.</summary>
    [RelayCommand]
    private void PlayTrack(FavouriteTrackItem item) => player.Play(AllTracks, Math.Max(0, Tracks.IndexOf(item)));

    [RelayCommand]
    private void OpenAlbum(AlbumItem item) => navigation.NavigateTo(PageKeys.Album, item.Album.Id);

    [RelayCommand]
    private void OpenArtist(ArtistItem item) => navigation.NavigateTo(PageKeys.Artist, item.Artist.Id);

    /// <summary>Removes the song's heart; the row goes at once and comes back if the server refuses.</summary>
    [RelayCommand]
    private async Task UnfavouriteTrackAsync(FavouriteTrackItem item)
    {
        var index = Tracks.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        Tracks.RemoveAt(index);
        UpdateFlags();
        if (!await favourites.SetAsync(item.Track.Id, false))
        {
            Tracks.Insert(Math.Min(index, Tracks.Count), item);
            UpdateFlags();
        }
    }

    private void OnFavouriteChanged(FavouriteChangedMessage message)
    {
        if (message.IsFavourite)
        {
            return;
        }

        Remove(Tracks, t => t.Track.Id == message.ItemId);
        Remove(Albums, a => a.Album.Id == message.ItemId);
        Remove(Artists, a => a.Artist.Id == message.ItemId);
        UpdateFlags();
    }

    private static void Remove<T>(ObservableCollection<T> items, Func<T, bool> match)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (match(items[i]))
            {
                items.RemoveAt(i);
            }
        }
    }

    private void UpdateFlags()
    {
        HasTracks = Tracks.Count > 0;
        HasAlbums = Albums.Count > 0;
        HasArtists = Artists.Count > 0;
    }
}
