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

/// <summary>One track row.</summary>
public sealed partial class TrackItem(Track track, string? artist, bool isFavourite = false) : ObservableObject, ICurrentTrackRow
{
    public Track Track { get; } = track;

    /// <summary>Gets or sets whether the row's track is playing (#247).</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>Gets the artists shown in the row, or <see langword="null"/> when they are the album's.</summary>
    public string? Artist { get; } = artist;

    /// <summary>Gets or sets the row's heart; kept in line with the server by the page.</summary>
    [ObservableProperty]
    public partial bool IsFavourite { get; set; } = isFavourite;

    public string Number => Track.TrackNumber?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    public string Name => Track.Name;

    public string Duration => Durations.Track(Track.Duration);

    public bool HasArtist => !string.IsNullOrEmpty(Artist);

    /// <summary>Gets the track's artists as links; empty when the row does not show them.</summary>
    public IReadOnlyList<ArtistRef> ArtistLinks => HasArtist ? Track.Artists : [];

    /// <summary>Gets the text Narrator reads, e.g. "3, Digital Love, 4:58".</summary>
    public string AccessibleName =>
        string.Join(", ", new[] { Number, Name, Artist ?? string.Empty, Duration }.Where(s => s.Length > 0));
}

/// <summary>The tracks of one disc. <see cref="Header"/> is empty for single-disc albums.</summary>
public sealed record DiscGroup(string Header, IReadOnlyList<TrackItem> Tracks)
{
    public bool HasHeader => Header.Length > 0;
}

/// <summary>
/// Album detail page: hero header (cover, title, artists, year, track count, length), tracks grouped by
/// disc, favourite, and the playback actions that M2 wires up.
/// </summary>
/// <summary>The column the album page orders its tracks by.</summary>
public enum AlbumTrackSort
{
    /// <summary>Disc and track number: the album order, in disc groups.</summary>
    Number,

    Title,

    Duration,

    /// <summary>Favourites first (reversed: last).</summary>
    Favourite,
}

public sealed partial class AlbumDetailViewModel(
    ILibraryService library,
    IPlayer player,
    INavigationService navigation,
    IErrorReporter errors,
    IStringResources strings,
    FavouritesService favourites,
    IMessenger messenger,
    InstantMix instantMix,
    CurrentTrack currentTrack) : ObservableObject
{
    private IReadOnlyList<Track> _tracks = [];

    /// <summary>The tracks in the order shown, which playing follows.</summary>
    private IReadOnlyList<Track> _shown = [];

    /// <summary>Cover width in device-independent pixels.</summary>
    public const double CoverWidth = 232;

    /// <summary>Background width: small on purpose, it is blurred and stretched.</summary>
    public const double BackgroundWidth = 128;

    public ObservableCollection<DiscGroup> Discs { get; } = [];

    /// <summary>Gets the album's tracks in play order (empty until loaded).</summary>
    public IReadOnlyList<Track> Tracks => _tracks;

    public bool HasTracks => _tracks.Count > 0;

    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlbum), nameof(Genres), nameof(HasGenres))]
    [NotifyPropertyChangedFor(nameof(MusicBrainzUrl), nameof(HasMusicBrainz), nameof(LastFmUrl), nameof(HasLastFm))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavouriteCommand), nameof(InstantMixCommand))]
    public partial Album? Album { get; private set; }

    public bool HasAlbum => Album is not null;

    [ObservableProperty]
    public partial ImageRequest? Cover { get; private set; }

    [ObservableProperty]
    public partial ImageRequest? Background { get; private set; }

    [ObservableProperty]
    public partial string Details { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Gets a value indicating whether loading failed; the page then shows a message instead of the album.</summary>
    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    [ObservableProperty]
    public partial bool IsFavourite { get; private set; }

    public string Title => Album?.Name ?? string.Empty;

    public IReadOnlyList<ArtistRef> Artists => Album?.AlbumArtists ?? [];

    public string ArtistText => Album?.AlbumArtist ?? string.Empty;

    public IReadOnlyList<Genre> Genres => Album?.Genres ?? [];

    public bool HasGenres => Genres.Count > 0;

    /// <summary>Gets the column the tracks are ordered by.</summary>
    [ObservableProperty]
    public partial AlbumTrackSort SortBy { get; private set; }

    /// <summary>Gets a value indicating whether the order is reversed.</summary>
    [ObservableProperty]
    public partial bool SortDescending { get; private set; }

    public string NumberSortGlyph => SortGlyph(AlbumTrackSort.Number);

    public string TitleSortGlyph => SortGlyph(AlbumTrackSort.Title);

    public string DurationSortGlyph => SortGlyph(AlbumTrackSort.Duration);

    public string FavouriteSortGlyph => SortGlyph(AlbumTrackSort.Favourite);

    public string NumberSortName => SortName(AlbumTrackSort.Number, "AlbumSort_Number");

    public string TitleSortName => SortName(AlbumTrackSort.Title, "AlbumSort_Title");

    public string DurationSortName => SortName(AlbumTrackSort.Duration, "AlbumSort_Duration");

    public string FavouriteSortName => SortName(AlbumTrackSort.Favourite, "AlbumSort_Favourite");

    /// <summary>Gets the album on MusicBrainz, when the server knows its ID.</summary>
    public Uri? MusicBrainzUrl => Album is null ? null : ExternalLinks.MusicBrainz(Album);

    public bool HasMusicBrainz => MusicBrainzUrl is not null;

    /// <summary>Gets the album on Last.fm.</summary>
    public Uri? LastFmUrl => Album is null ? null : ExternalLinks.LastFm(Album);

    public bool HasLastFm => LastFmUrl is not null;

    public async Task LoadAsync(string albumId, CancellationToken cancellationToken = default)
    {
        // Weak: the page's view model needs no unregistering.
        if (!messenger.IsRegistered<FavouriteChangedMessage>(this))
        {
            messenger.Register<AlbumDetailViewModel, FavouriteChangedMessage>(this, static (r, m) => r.OnFavouriteChanged(m));
        }

        IsLoading = true;
        LoadFailed = false;
        try
        {
            var albumTask = library.GetAlbumAsync(albumId, cancellationToken);
            var tracksTask = library.GetAlbumTracksAsync(albumId, cancellationToken);
            var album = await albumTask;
            var tracks = await tracksTask;

            Album = album;
            IsFavourite = favourites.IsFavourite(album.Id, album.IsFavorite);
            Cover = ImageRequest.Primary(album.Id, album.PrimaryImageTag, CoverWidth, DisplayScale);
            Background = ImageRequest.Primary(album.Id, album.PrimaryImageTag, BackgroundWidth, 1);
            Details = BuildDetails(album, tracks, strings);
            _tracks = tracks;
            BuildDiscs(album, tracks);
            OnPropertyChanged(nameof(Tracks));
            OnPropertyChanged(nameof(HasTracks));
            PlayCommand.NotifyCanExecuteChanged();
            ShuffleCommand.NotifyCanExecuteChanged();
            AddToQueueCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Artists));
            OnPropertyChanged(nameof(ArtistText));
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadAlbum);
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

    [RelayCommand(CanExecute = nameof(HasAlbum))]
    private async Task ToggleFavouriteAsync()
    {
        var album = Album!;
        var target = !IsFavourite;

        // Optimistic: show the new state at once, roll back if the server refuses.
        IsFavourite = target;
        if (await favourites.SetAsync(album.Id, target))
        {
            Album = album with { IsFavorite = target };
        }
        else
        {
            IsFavourite = !target;
        }
    }

    private bool CanPlay() => _tracks.Count > 0;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play() => player.Play(_shown);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Shuffle() => player.Shuffle(_tracks);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void AddToQueue() => player.AddToQueue(_shown);

    [RelayCommand(CanExecute = nameof(HasAlbum))]
    private Task InstantMixAsync() => instantMix.StartAsync(Album!.Id);

    /// <summary>Plays the album from <paramref name="item"/> on (a track row was clicked).</summary>
    [RelayCommand]
    private void OpenGenre(Genre genre) => navigation.NavigateTo(PageKeys.Genre, genre.Id);

    [RelayCommand]
    private void PlayTrack(TrackItem item)
    {
        var index = _shown.ToList().IndexOf(item.Track);
        if (index >= 0)
        {
            player.Play(_shown, index);
        }
    }

    [RelayCommand]
    private void OpenArtist(ArtistRef artist) => navigation.NavigateTo(PageKeys.Artist, artist.Id);

    /// <summary>Toggles a track's heart, optimistic like the album's.</summary>
    [RelayCommand]
    private async Task ToggleTrackFavouriteAsync(TrackItem item)
    {
        var target = !item.IsFavourite;
        item.IsFavourite = target;
        if (!await favourites.SetAsync(item.Track.Id, target))
        {
            item.IsFavourite = !target;
        }
    }

    /// <summary>A heart was toggled elsewhere (now playing bar, favourites page, a context menu).</summary>
    private void OnFavouriteChanged(FavouriteChangedMessage message)
    {
        if (Album?.Id == message.ItemId)
        {
            IsFavourite = message.IsFavourite;
        }

        foreach (var item in Discs.SelectMany(d => d.Tracks).Where(t => t.Track.Id == message.ItemId))
        {
            item.IsFavourite = message.IsFavourite;
        }
    }

    /// <summary>Year, track count and length, e.g. "2001 · 14 tracks · 61 min"; also the album popup's (#185).</summary>
    internal static string BuildDetails(Album album, IReadOnlyList<Track> tracks, IStringResources strings)
    {
        var parts = new List<string>();
        if (album.Year is { } year)
        {
            parts.Add(year.ToString(CultureInfo.CurrentCulture));
        }

        parts.Add(strings.GetString(tracks.Count == 1 ? "Album_TrackCountOne" : "Album_TrackCount")
            .Replace("{0}", tracks.Count.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal));

        var total = album.Duration ?? TimeSpan.FromTicks(tracks.Sum(t => t.Duration?.Ticks ?? 0));
        if (total > TimeSpan.Zero)
        {
            parts.Add(Durations.Total(total, strings));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Orders by <paramref name="column"/>; the same column again reverses the order.</summary>
    [RelayCommand]
    private void Sort(AlbumTrackSort column)
    {
        SortDescending = column == SortBy && !SortDescending;
        SortBy = column;
        if (Album is { } album)
        {
            BuildDiscs(album, _tracks);
        }

        OnPropertyChanged(nameof(NumberSortGlyph));
        OnPropertyChanged(nameof(TitleSortGlyph));
        OnPropertyChanged(nameof(DurationSortGlyph));
        OnPropertyChanged(nameof(FavouriteSortGlyph));
        OnPropertyChanged(nameof(NumberSortName));
        OnPropertyChanged(nameof(TitleSortName));
        OnPropertyChanged(nameof(DurationSortName));
        OnPropertyChanged(nameof(FavouriteSortName));
    }

    /// <summary>Chevron up or down on the sorted column, nothing on the others.</summary>
    private string SortGlyph(AlbumTrackSort column) =>
        column != SortBy ? string.Empty : SortDescending ? "\uE70D" : "\uE70E";

    /// <summary>The header's accessible name: the column, and how it is sorted if it is.</summary>
    private string SortName(AlbumTrackSort column, string columnKey)
    {
        var name = strings.GetString(columnKey);
        if (column != SortBy)
        {
            return strings.GetString("AlbumSort_SortBy").Replace("{0}", name, StringComparison.Ordinal);
        }

        return strings.GetString(SortDescending ? "AlbumSort_Descending" : "AlbumSort_Ascending").Replace("{0}", name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fills <see cref="Discs"/> in the chosen order: by number in disc groups (as the album is), by any
    /// other column in one list. Ties keep the album order.
    /// </summary>
    private void BuildDiscs(Album album, IReadOnlyList<Track> tracks)
    {
        Discs.Clear();
        var items = tracks.Select(t => new TrackItem(t, TrackArtist(album, t), favourites.IsFavourite(t.Id, t.IsFavorite))).ToList();

        if (SortBy == AlbumTrackSort.Number)
        {
            var groups = items.GroupBy(i => i.Track.DiscNumber ?? 1).OrderBy(g => g.Key).ToList();
            var multiDisc = groups.Count > 1;
            if (SortDescending)
            {
                groups.Reverse();
            }

            foreach (var group in groups)
            {
                var header = multiDisc
                    ? strings.GetString("Album_Disc").Replace("{0}", group.Key.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
                    : string.Empty;
                var discItems = group.ToList();
                if (SortDescending)
                {
                    discItems.Reverse();
                }

                Discs.Add(new DiscGroup(header, discItems));
            }
        }
        else
        {
            Discs.Add(new DiscGroup(string.Empty, Ordered(items)));
        }

        _shown = Discs.SelectMany(d => d.Tracks).Select(i => i.Track).ToList();
        currentTrack.Watch(this, () => Discs.SelectMany(d => d.Tracks));
    }

    private List<TrackItem> Ordered(List<TrackItem> items)
    {
        Comparison<TrackItem> compare = SortBy switch
        {
            AlbumTrackSort.Title => (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase),
            AlbumTrackSort.Duration => (a, b) => (a.Track.Duration ?? TimeSpan.Zero).CompareTo(b.Track.Duration ?? TimeSpan.Zero),
            AlbumTrackSort.Favourite => (a, b) => b.IsFavourite.CompareTo(a.IsFavourite),
            _ => (_, _) => 0,
        };
        var sign = SortDescending ? -1 : 1;

        // Order is stable: equal values keep the album order, in both directions.
        return items.Order(Comparer<TrackItem>.Create((a, b) => sign * compare(a, b))).ToList();
    }

    /// <summary>Shows a track's artists only when they differ from the album artist (compilations, features).</summary>
    internal static string? TrackArtist(Album album, Track track)
    {
        var names = string.Join(", ", track.Artists.Select(a => a.Name));
        return names.Length == 0 || string.Equals(names, album.AlbumArtist, StringComparison.OrdinalIgnoreCase) ? null : names;
    }
}
