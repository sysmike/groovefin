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

/// <summary>One track row.</summary>
public sealed record TrackItem(Track Track, string? Artist)
{
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
public sealed partial class AlbumDetailViewModel(
    ILibraryService library,
    IPlayer player,
    INavigationService navigation,
    IErrorReporter errors,
    IStringResources strings) : ObservableObject
{
    private IReadOnlyList<Track> _tracks = [];

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
    [NotifyPropertyChangedFor(nameof(HasAlbum))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavouriteCommand))]
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

    public async Task LoadAsync(string albumId, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        LoadFailed = false;
        try
        {
            var albumTask = library.GetAlbumAsync(albumId, cancellationToken);
            var tracksTask = library.GetAlbumTracksAsync(albumId, cancellationToken);
            var album = await albumTask;
            var tracks = await tracksTask;

            Album = album;
            IsFavourite = album.IsFavorite;
            Cover = ImageRequest.Primary(album.Id, album.PrimaryImageTag, CoverWidth, DisplayScale);
            Background = ImageRequest.Primary(album.Id, album.PrimaryImageTag, BackgroundWidth, 1);
            Details = BuildDetails(album, tracks);
            BuildDiscs(album, tracks);
            _tracks = tracks;
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
        try
        {
            await library.SetFavouriteAsync(album.Id, target);
            Album = album with { IsFavorite = target };
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            IsFavourite = !target;
            errors.Report(ex, Operations.SetFavourite);
        }
    }

    private bool CanPlay() => _tracks.Count > 0;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play() => player.Play(_tracks);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Shuffle() => player.Shuffle(_tracks);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void AddToQueue() => player.AddToQueue(_tracks);

    /// <summary>Plays the album from <paramref name="item"/> on (a track row was clicked).</summary>
    [RelayCommand]
    private void PlayTrack(TrackItem item)
    {
        var index = _tracks.ToList().IndexOf(item.Track);
        if (index >= 0)
        {
            player.Play(_tracks, index);
        }
    }

    [RelayCommand]
    private void OpenArtist(ArtistRef artist) => navigation.NavigateTo(PageKeys.Artist, artist.Id);

    private string BuildDetails(Album album, IReadOnlyList<Track> tracks)
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

    private void BuildDiscs(Album album, IReadOnlyList<Track> tracks)
    {
        Discs.Clear();
        var groups = tracks.GroupBy(t => t.DiscNumber ?? 1).OrderBy(g => g.Key).ToList();
        var multiDisc = groups.Count > 1;

        foreach (var group in groups)
        {
            var header = multiDisc
                ? strings.GetString("Album_Disc").Replace("{0}", group.Key.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
                : string.Empty;
            var items = group.Select(t => new TrackItem(t, TrackArtist(album, t))).ToList();
            Discs.Add(new DiscGroup(header, items));
        }
    }

    /// <summary>Shows a track's artists only when they differ from the album artist (compilations, features).</summary>
    private static string? TrackArtist(Album album, Track track)
    {
        var names = string.Join(", ", track.Artists.Select(a => a.Name));
        return names.Length == 0 || string.Equals(names, album.AlbumArtist, StringComparison.OrdinalIgnoreCase) ? null : names;
    }
}
