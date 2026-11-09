using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Mixes;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// A mix's page (#401): its rules in a line and the songs they pick now, in On repeat's table with each song's plays.
/// Play, shuffle and queue them, pick again, save them as a Jellyfin playlist (a snapshot, which then opens), change
/// the rules or delete the mix.
/// </summary>
public sealed partial class MixViewModel(
    MixStore store,
    MixEditing editing,
    MixBuilder builder,
    IPlaylistService playlists,
    IPlayer player,
    INavigationService navigation,
    IDialogService dialogs,
    IErrorReporter errors,
    IStringResources strings,
    FavouritesService favourites,
    IMessenger messenger,
    CurrentTrack currentTrack) : ObservableObject
{
    private Mix? _mix;
    private int _load;

    public ObservableCollection<OnRepeatItem> Entries { get; } = currentTrack.Watch(new ObservableCollection<OnRepeatItem>());

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(EditCommand), nameof(DeleteCommand))]
    public partial bool HasMix { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    /// <summary>Gets a value indicating whether the mix was not found: deleted, or another user's.</summary>
    [ObservableProperty]
    public partial bool IsMissing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(ShuffleCommand), nameof(AddToQueueCommand), nameof(SaveAsPlaylistCommand))]
    public partial bool HasEntries { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoaded { get; private set; }

    /// <summary>Gets a value indicating whether the rules match no song now.</summary>
    public bool IsEmpty => IsLoaded && !HasEntries;

    private IReadOnlyList<Models.Track> Tracks => [.. Entries.Select(e => e.Track)];

    /// <summary>Shows the mix with <paramref name="mixId"/> and picks its songs.</summary>
    public async Task LoadAsync(string? mixId, CancellationToken cancellationToken = default)
    {
        // Weak: the page's view model needs no unregistering.
        if (!messenger.IsRegistered<FavouriteChangedMessage>(this))
        {
            messenger.Register<MixViewModel, FavouriteChangedMessage>(this, static (r, m) => r.OnFavouriteChanged(m));
        }

        _mix = mixId is null ? null : store.Find(mixId);
        HasMix = _mix is not null;
        IsMissing = _mix is null;
        if (_mix is null)
        {
            Name = string.Empty;
            Summary = string.Empty;
            Entries.Clear();
            HasEntries = false;
            return;
        }

        Name = _mix.Rules.Name;
        Summary = MixSummary.Describe(_mix.Rules, strings);
        await PickAsync(cancellationToken);
    }

    /// <summary>Picks the songs again: a random mix changes, the others follow what was played and added.</summary>
    [RelayCommand(CanExecute = nameof(HasMix))]
    private Task RefreshAsync() => PickAsync(CancellationToken.None);

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void Play() => player.Play(Tracks);

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void Shuffle() => player.Shuffle(Tracks);

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void AddToQueue() => player.AddToQueue(Tracks);

    /// <summary>Plays the songs from <paramref name="item"/> on (a row was clicked).</summary>
    [RelayCommand]
    private void PlayEntry(OnRepeatItem item) => player.Play(Tracks, Math.Max(0, Entries.IndexOf(item)));

    /// <summary>Saves the songs as a Jellyfin playlist named after the mix, then opens it. The playlist does not change later.</summary>
    [RelayCommand(CanExecute = nameof(HasEntries))]
    private async Task SaveAsPlaylistAsync()
    {
        try
        {
            var id = await playlists.CreateAsync(Name, [.. Entries.Select(e => e.Track.Id)]);
            navigation.NavigateTo(PageKeys.Playlist, id);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            errors.Report(ex, Operations.SavePlaylist);
        }
    }

    /// <summary>Changes the rules with the editor, then picks the songs again.</summary>
    [RelayCommand(CanExecute = nameof(HasMix))]
    private async Task EditAsync()
    {
        if (await editing.EditAsync(_mix) is { } changed)
        {
            await LoadAsync(changed.Id);
        }
    }

    /// <summary>Deletes the mix after asking, and goes back.</summary>
    [RelayCommand(CanExecute = nameof(HasMix))]
    private async Task DeleteAsync()
    {
        if (_mix is { } mix && await MixDeletion.ConfirmAsync(dialogs, strings, mix.Rules.Name))
        {
            store.Delete(mix.Id);
            navigation.NavigateTo(PageKeys.Mixes);
        }
    }

    /// <summary>Toggles a song's heart, optimistic like the album page's.</summary>
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

    private async Task PickAsync(CancellationToken cancellationToken)
    {
        var rules = _mix!.Rules;
        var load = ++_load;
        IsLoading = true;
        LoadFailed = false;
        try
        {
            var tracks = await builder.BuildAsync(rules, cancellationToken);
            if (load != _load)
            {
                return;
            }

            Entries.Clear();
            foreach (var track in tracks)
            {
                var plays = strings.GetString("OnRepeat_Plays").Replace("{0}", track.PlayCount.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
                Entries.Add(new OnRepeatItem(track, Entries.Count + 1, track.PlayCount, plays, favourites.IsFavourite(track.Id, track.IsFavorite)));
            }

            HasEntries = Entries.Count > 0;
            IsLoaded = true;
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadMix);
        }
        catch (OperationCanceledException)
        {
            // The page was closed.
        }
        finally
        {
            if (load == _load)
            {
                IsLoading = false;
            }
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
