using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// The songs popup of an album tile (the down arrow on the cover, #185): the album's tracks in disc and
/// track order, to play from one of them or from the start without opening the album page.
/// </summary>
public sealed partial class AlbumPreviewViewModel(
    ILibraryService library,
    IPlayer player,
    IErrorReporter errors,
    IStringResources strings) : ObservableObject
{
    private IReadOnlyList<Track> _tracks = [];

    [ObservableProperty]
    public partial Album? Album { get; private set; }

    /// <summary>Gets the tracks, one group per disc; a group's header is empty for single-disc albums.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DiscGroup> Discs { get; private set; } = [];

    /// <summary>Gets year, track count and length once the tracks are loaded.</summary>
    [ObservableProperty]
    public partial string Details { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    public string Title => Album?.Name ?? string.Empty;

    public string ArtistText => Album?.AlbumArtist ?? string.Empty;

    /// <summary>Gets the play button's accessible name, e.g. "Play Discovery".</summary>
    public string PlayName => strings.GetString("Cover_Play").Replace("{0}", Title, StringComparison.Ordinal);

    public async Task LoadAsync(Album album, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(album);
        Album = album;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ArtistText));
        OnPropertyChanged(nameof(PlayName));
        IsLoading = true;
        LoadFailed = false;
        try
        {
            var tracks = await library.GetAlbumTracksAsync(album.Id, cancellationToken);
            Discs = BuildDiscs(album, tracks);
            _tracks = [.. Discs.SelectMany(d => d.Tracks).Select(i => i.Track)];
            Details = AlbumDetailViewModel.BuildDetails(album, tracks, strings);
            PlayCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadAlbum);
        }
        catch (OperationCanceledException)
        {
            // The popup was closed.
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanPlay() => _tracks.Count > 0;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play() => player.Play(_tracks);

    /// <summary>Plays the album from <paramref name="item"/> on (a song in the popup was clicked).</summary>
    [RelayCommand]
    private void PlayTrack(TrackItem item)
    {
        var index = _tracks.ToList().IndexOf(item.Track);
        if (index >= 0)
        {
            player.Play(_tracks, index);
        }
    }

    private List<DiscGroup> BuildDiscs(Album album, IReadOnlyList<Track> tracks)
    {
        var groups = tracks
            .Select(t => new TrackItem(t, AlbumDetailViewModel.TrackArtist(album, t)))
            .GroupBy(i => i.Track.DiscNumber ?? 1)
            .OrderBy(g => g.Key)
            .ToList();
        var multiDisc = groups.Count > 1;
        return [.. groups.Select(g => new DiscGroup(
            multiDisc ? strings.GetString("Album_Disc").Replace("{0}", g.Key.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal) : string.Empty,
            [.. g]))];
    }
}
