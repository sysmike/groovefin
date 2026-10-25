using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Formatting;
using Groovefin.Core.History;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>A row of "On repeat" (#251): rank, title, artists, album, plays, heart, length.</summary>
public sealed partial class OnRepeatItem(Track track, int rank, int plays, string playsText, bool isFavourite = false) : ObservableObject, ICurrentTrackRow
{
    public Track Track { get; } = track;

    /// <summary>Gets or sets whether the row's track is playing (#247).</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>Gets or sets the row's heart; kept in line with the server by the view model.</summary>
    [ObservableProperty]
    public partial bool IsFavourite { get; set; } = isFavourite;

    public int Rank { get; } = rank;

    /// <summary>Gets how often the track was played in the last 30 days.</summary>
    public int Plays { get; } = plays;

    public string Number => Rank.ToString(CultureInfo.CurrentCulture);

    public string Name => Track.Name;

    public string Artist => string.Join(", ", Track.Artists.Select(a => a.Name));

    public string Album => Track.AlbumName ?? string.Empty;

    /// <summary>Gets the plays for the Plays column, e.g. "12".</summary>
    public string PlaysCount => Plays.ToString(CultureInfo.CurrentCulture);

    public string Duration => Durations.Track(Track.Duration);

    /// <summary>Gets the text Narrator reads, e.g. "1, One More Time, Daft Punk, Discovery, 12 plays, 5:20".</summary>
    public string AccessibleName => string.Join(", ", new[] { Number, Name, Artist, Album, playsText, Duration }.Where(s => s.Length > 0));
}

/// <summary>
/// "On repeat" (#251): the tracks played most in the last 30 days, from the play history. Opening the page
/// first reads the plays on other devices from the server. Play, shuffle and queue the list, play from a row,
/// toggle a heart, or save the list as a playlist (which then opens).
/// </summary>
public sealed partial class OnRepeatViewModel(
    PlayHistory history,
    ILibraryService library,
    IPlaylistService playlists,
    IPlayer player,
    INavigationService navigation,
    IErrorReporter errors,
    IStringResources strings,
    FavouritesService favourites,
    IMessenger messenger,
    CurrentTrack currentTrack,
    TimeProvider time) : ObservableObject
{
    public ObservableCollection<OnRepeatItem> Entries { get; } = currentTrack.Watch(new ObservableCollection<OnRepeatItem>());

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(ShuffleCommand), nameof(AddToQueueCommand), nameof(SaveAsPlaylistCommand))]
    public partial bool HasEntries { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoaded { get; private set; }

    /// <summary>Gets a value indicating whether the list loaded and has nothing yet.</summary>
    public bool IsEmpty => IsLoaded && !HasEntries;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // Weak: the page's view model needs no unregistering.
        if (!messenger.IsRegistered<FavouriteChangedMessage>(this))
        {
            messenger.Register<OnRepeatViewModel, FavouriteChangedMessage>(this, static (r, m) => r.OnFavouriteChanged(m));
        }

        IsLoading = true;
        LoadFailed = false;
        try
        {
            await history.SyncAsync();
            var ranked = await history.GetOnRepeatAsync();
            var tracks = (await library.GetTracksByIdAsync([.. ranked.Select(e => e.TrackId)], cancellationToken))
                .ToDictionary(t => t.Id, StringComparer.Ordinal);

            Entries.Clear();
            foreach (var entry in ranked.Where(e => tracks.ContainsKey(e.TrackId)))
            {
                var track = tracks[entry.TrackId];
                var plays = strings.GetString("OnRepeat_Plays").Replace("{0}", entry.Plays.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
                Entries.Add(new OnRepeatItem(track, Entries.Count + 1, entry.Plays, plays, favourites.IsFavourite(track.Id, track.IsFavorite)));
            }

            HasEntries = Entries.Count > 0;
            IsLoaded = true;
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadOnRepeat);
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

    private IReadOnlyList<Track> Tracks => [.. Entries.Select(e => e.Track)];

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void Play() => player.Play(Tracks);

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void Shuffle() => player.Shuffle(Tracks);

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void AddToQueue() => player.AddToQueue(Tracks);

    /// <summary>Plays the list from <paramref name="item"/> on (a row was clicked).</summary>
    [RelayCommand]
    private void PlayEntry(OnRepeatItem item) => player.Play(Tracks, Math.Max(0, Entries.IndexOf(item)));

    /// <summary>Saves the list as a playlist named after today, then opens it.</summary>
    [RelayCommand(CanExecute = nameof(HasEntries))]
    private async Task SaveAsPlaylistAsync()
    {
        var date = time.GetLocalNow().ToString("d", CultureInfo.CurrentCulture);
        var name = strings.GetString("OnRepeat_PlaylistName").Replace("{0}", date, StringComparison.Ordinal);
        try
        {
            var id = await playlists.CreateAsync(name, [.. Entries.Select(e => e.Track.Id)]);
            navigation.NavigateTo(PageKeys.Playlist, id);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            errors.Report(ex, Operations.SavePlaylist);
        }
    }

    /// <summary>Toggles a track's heart, optimistic like the album page's.</summary>
    [RelayCommand]
    private async Task ToggleEntryFavouriteAsync(OnRepeatItem item)
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
        foreach (var item in Entries.Where(e => e.Track.Id == message.ItemId))
        {
            item.IsFavourite = message.IsFavourite;
        }
    }
}
