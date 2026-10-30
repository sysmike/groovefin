using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Menus;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Menus;

public sealed class ContextMenusTests
{
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly FavouritesService _favourites;
    private readonly ContextMenus _sut;

    public ContextMenusTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Menu_GoToArtist").Returns("Go to {0}");
        _favourites = new FavouritesService(_library, new WeakReferenceMessenger(), _errors);
        _sut = new ContextMenus(_player, _library, _playlists, _navigation, _favourites, new InstantMix(_library, _player, new InMemorySettingsService(), _errors), _dialogs, _errors, _strings);
    }

    private static readonly Track Song = new()
    {
        Id = "t1",
        Name = "Digital Love",
        AlbumId = "al1",
        AlbumName = "Discovery",
        Artists = [new ArtistRef("ar1", "Daft Punk"), new ArtistRef("ar2", "Romanthony")],
        Duration = TimeSpan.FromSeconds(298),
        TrackNumber = 3,
        PlayCount = 7,
    };

    private static readonly Track[] AlbumTracks = [new() { Id = "a1", Name = "One" }, new() { Id = "a2", Name = "Two" }];

    private static MenuAction Find(IReadOnlyList<MenuAction> actions, MenuActionKind kind) => actions.Single(a => a.Kind == kind);

    [Fact]
    public void A_track_has_every_action_and_one_go_to_per_artist()
    {
        var actions = _sut.ForTrack(Song);

        actions.Select(a => a.Kind).Should().Equal(
            MenuActionKind.PlayNow,
            MenuActionKind.PlayNext,
            MenuActionKind.AddToQueue,
            MenuActionKind.AddToPlaylist,
            MenuActionKind.GoToAlbum,
            MenuActionKind.GoToArtist,
            MenuActionKind.GoToArtist,
            MenuActionKind.InstantMix,
            MenuActionKind.Favourite,
            MenuActionKind.ShowInfo);
        actions.Where(a => a.Kind == MenuActionKind.GoToArtist).Select(a => a.Text).Should().Equal("Go to Daft Punk", "Go to Romanthony");
    }

    [Fact]
    public async Task Several_tracks_get_playback_add_to_playlist_and_favourites()
    {
        var actions = _sut.ForTracks(AlbumTracks);

        actions.Select(a => a.Kind).Should().Equal(
            MenuActionKind.PlayNow, MenuActionKind.PlayNext, MenuActionKind.AddToQueue, MenuActionKind.AddToPlaylist, MenuActionKind.Favourite);
        (await Find(actions, MenuActionKind.AddToPlaylist).Tracks!()).Should().Equal(AlbumTracks);
        await Find(actions, MenuActionKind.PlayNext).InvokeAsync();
        _player.Received(1).PlayNext(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(AlbumTracks)));
    }

    [Fact]
    public async Task A_folder_plays_and_queues_its_tracks_with_subfolders()
    {
        _library.GetFolderTracksAsync("f1", ContextMenus.FolderTrackLimit, Arg.Any<CancellationToken>()).Returns(AlbumTracks);
        var actions = _sut.ForFolder(new Folder("f1", "Daft Punk"));

        actions.Select(a => a.Kind).Should().Equal(
            MenuActionKind.PlayNow, MenuActionKind.PlayNext, MenuActionKind.AddToQueue, MenuActionKind.AddToPlaylist);
        await Find(actions, MenuActionKind.AddToQueue).InvokeAsync();

        _player.Received(1).AddToQueue(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(AlbumTracks)));
    }

    [Fact]
    public void A_single_selected_track_gets_the_track_menu() =>
        _sut.ForTracks([Song]).Select(a => a.Kind).Should().Equal(_sut.ForTrack(Song).Select(a => a.Kind));

    [Fact]
    public void A_track_without_album_has_no_go_to_album()
    {
        _sut.ForTrack(Song with { AlbumId = null }).Should().NotContain(a => a.Kind == MenuActionKind.GoToAlbum);
    }

    [Fact]
    public async Task Track_playback_actions_use_the_track()
    {
        var actions = _sut.ForTrack(Song);

        await Find(actions, MenuActionKind.PlayNow).InvokeAsync();
        await Find(actions, MenuActionKind.PlayNext).InvokeAsync();
        await Find(actions, MenuActionKind.AddToQueue).InvokeAsync();

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Single() == Song));
        _player.Received(1).PlayNext(Arg.Is<IReadOnlyList<Track>>(t => t.Single() == Song));
        _player.Received(1).AddToQueue(Arg.Is<IReadOnlyList<Track>>(t => t.Single() == Song));
        (await Find(actions, MenuActionKind.AddToPlaylist).Tracks!()).Should().Equal(Song);
    }

    [Fact]
    public async Task Go_to_actions_navigate()
    {
        var actions = _sut.ForTrack(Song);

        await Find(actions, MenuActionKind.GoToAlbum).InvokeAsync();
        await actions.First(a => a.Kind == MenuActionKind.GoToArtist).InvokeAsync();

        _navigation.Received(1).NavigateTo(PageKeys.Album, "al1");
        _navigation.Received(1).NavigateTo(PageKeys.Artist, "ar1");
    }

    [Fact]
    public async Task Instant_mix_replaces_the_queue_with_the_mix()
    {
        _library.GetInstantMixAsync("t1", InstantMix.Size, Arg.Any<CancellationToken>()).Returns(AlbumTracks);

        await Find(_sut.ForTrack(Song), MenuActionKind.InstantMix).InvokeAsync();

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(AlbumTracks)));
    }

    [Fact]
    public async Task An_empty_mix_plays_nothing_and_a_failed_one_is_reported()
    {
        _library.GetInstantMixAsync("t1", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        await Find(_sut.ForTrack(Song), MenuActionKind.InstantMix).InvokeAsync();
        _player.DidNotReceiveWithAnyArgs().Play(default!);

        _library.GetInstantMixAsync("t1", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Track>>(_ => throw new JellyfinException("boom"));
        await Find(_sut.ForTrack(Song), MenuActionKind.InstantMix).InvokeAsync();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.Playback);
    }

    [Fact]
    public async Task Favourite_toggles_and_the_next_menu_offers_the_opposite()
    {
        await Find(_sut.ForTrack(Song), MenuActionKind.Favourite).InvokeAsync();

        await _library.Received(1).SetFavouriteAsync("t1", true, Arg.Any<CancellationToken>());
        var next = _sut.ForTrack(Song);
        next.Should().ContainSingle(a => a.Kind == MenuActionKind.Unfavourite).Which.Text.Should().Be("Menu_Unfavourite");
        await Find(next, MenuActionKind.Unfavourite).InvokeAsync();
        await _library.Received(1).SetFavouriteAsync("t1", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Several_tracks_become_favourites_and_only_the_others_are_sent()
    {
        Track[] tracks = [new() { Id = "a1", Name = "a1", IsFavorite = true }, new() { Id = "a2", Name = "a2" }, new() { Id = "a3", Name = "a3" }];

        var favourite = Find(_sut.ForTracks(tracks), MenuActionKind.Favourite);
        favourite.Text.Should().Be("Menu_Favourite", "one of them is not a favourite yet");
        await favourite.InvokeAsync();

        await _library.DidNotReceive().SetFavouriteAsync("a1", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _library.Received(1).SetFavouriteAsync("a2", true, Arg.Any<CancellationToken>());
        await _library.Received(1).SetFavouriteAsync("a3", true, Arg.Any<CancellationToken>());
        _sut.ForTracks(tracks).Should().ContainSingle(a => a.Kind == MenuActionKind.Unfavourite, "now all are favourites");
    }

    [Fact]
    public async Task Several_favourites_are_all_removed()
    {
        Track[] tracks = [new() { Id = "a1", Name = "a1", IsFavorite = true }, new() { Id = "a2", Name = "a2", IsFavorite = true }];

        var unfavourite = Find(_sut.ForTracks(tracks), MenuActionKind.Unfavourite);
        unfavourite.Text.Should().Be("Menu_Unfavourite");
        await unfavourite.InvokeAsync();

        await _library.Received(1).SetFavouriteAsync("a1", false, Arg.Any<CancellationToken>());
        await _library.Received(1).SetFavouriteAsync("a2", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Show_info_loads_the_tracks_details_when_it_opens()
    {
        var details = new TrackDetails { Track = Song with { PlayCount = 9 }, Codec = "flac" };
        _library.GetTrackDetailsAsync("t1", Arg.Any<CancellationToken>()).Returns(details);
        var action = Find(_sut.ForTrack(Song), MenuActionKind.ShowInfo);
        await _library.DidNotReceiveWithAnyArgs().GetTrackDetailsAsync(default!, TestContext.Current.CancellationToken);

        await action.InvokeAsync();

        await _dialogs.Received(1).ShowDetailsAsync(
            "Digital Love",
            Arg.Is<IReadOnlyList<DetailSection>>(s =>
                s.SelectMany(x => x.Rows).Any(r => r.Key == "Info_PlayCount" && r.Value == "9")
                && s.SelectMany(x => x.Rows).Any(r => r.Key == "Info_Codec" && r.Value == "FLAC")),
            "Dialog_Close");
    }

    [Fact]
    public async Task Show_info_falls_back_to_the_loaded_track_when_the_server_fails()
    {
        _library.GetTrackDetailsAsync("t1", Arg.Any<CancellationToken>()).Returns<TrackDetails>(_ => throw new JellyfinException("offline"));

        await Find(_sut.ForTrack(Song), MenuActionKind.ShowInfo).InvokeAsync();

        await _dialogs.Received(1).ShowDetailsAsync(
            "Digital Love",
            Arg.Is<IReadOnlyList<DetailSection>>(s => s.Select(x => x.Heading).SequenceEqual(new[] { "Info_SectionTags", "Info_SectionLibrary" })
                && s[0].Rows.Select(p => $"{p.Key}={p.Value}").SequenceEqual(new[]
                {
                    "Info_Title=Digital Love",
                    "Info_Artists=Daft Punk, Romanthony",
                    "Info_Album=Discovery",
                    "Info_Track=3",
                    "Info_Length=4:58",
                })),
            "Dialog_Close");
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public async Task Album_actions_load_its_tracks_when_they_run()
    {
        var album = new Album { Id = "al1", Name = "Discovery", AlbumArtists = [new ArtistRef("ar1", "Daft Punk")], IsFavorite = true };
        _library.GetAlbumTracksAsync("al1", Arg.Any<CancellationToken>()).Returns(AlbumTracks);

        var actions = _sut.ForAlbum(album);
        await _library.DidNotReceiveWithAnyArgs().GetAlbumTracksAsync(default!, TestContext.Current.CancellationToken);
        await Find(actions, MenuActionKind.AddToQueue).InvokeAsync();

        _player.Received(1).AddToQueue(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(AlbumTracks)));
        actions.Select(a => a.Kind).Should().Equal(
            MenuActionKind.PlayNow, MenuActionKind.PlayNext, MenuActionKind.AddToQueue, MenuActionKind.AddToPlaylist,
            MenuActionKind.GoToArtist, MenuActionKind.InstantMix, MenuActionKind.Unfavourite);
    }

    [Fact]
    public async Task A_failed_album_load_is_reported()
    {
        _library.GetAlbumTracksAsync("al1", Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Track>>(_ => throw new JellyfinException("boom"));

        await Find(_sut.ForAlbum(new Album { Id = "al1", Name = "x" }), MenuActionKind.PlayNow).InvokeAsync();

        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.Playback);
        _player.DidNotReceiveWithAnyArgs().Play(default!);
    }

    [Fact]
    public async Task Artist_actions_use_all_their_tracks()
    {
        _library.GetArtistTracksAsync("ar1", Arg.Any<CancellationToken>()).Returns(AlbumTracks);

        var actions = _sut.ForArtist(new Artist { Id = "ar1", Name = "Daft Punk" });
        await Find(actions, MenuActionKind.PlayNext).InvokeAsync();

        _player.Received(1).PlayNext(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(AlbumTracks)));
        actions.Select(a => a.Kind).Should().Equal(
            MenuActionKind.PlayNow, MenuActionKind.PlayNext, MenuActionKind.AddToQueue, MenuActionKind.AddToPlaylist,
            MenuActionKind.InstantMix, MenuActionKind.Favourite);
    }

    [Fact]
    public async Task Playlist_actions_play_its_entries()
    {
        _playlists.GetEntriesAsync("p1", Arg.Any<CancellationToken>()).Returns(AlbumTracks.Select((t, i) => new PlaylistEntry($"e{i}", t)).ToList());

        var actions = _sut.ForPlaylist(new Playlist { Id = "p1", Name = "Chill" });
        await Find(actions, MenuActionKind.PlayNow).InvokeAsync();

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(AlbumTracks)));
        actions.Select(a => a.Kind).Should().Equal(MenuActionKind.PlayNow, MenuActionKind.PlayNext, MenuActionKind.AddToQueue, MenuActionKind.InstantMix);
    }

    [Fact]
    public async Task Add_to_playlist_has_nothing_to_run_itself()
    {
        await Find(_sut.ForTrack(Song), MenuActionKind.AddToPlaylist).InvokeAsync();

        _player.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Genre_actions_play_its_songs_and_mix_it()
    {
        _library.GetGenreTracksAsync("g1", ContextMenus.GenreTrackLimit, Arg.Any<CancellationToken>()).Returns(AlbumTracks);

        var actions = _sut.ForGenre(new Genre("g1", "Electronic"));
        await Find(actions, MenuActionKind.AddToQueue).InvokeAsync();

        _player.Received(1).AddToQueue(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(AlbumTracks)));
        actions.Select(a => a.Kind).Should().Equal(MenuActionKind.PlayNow, MenuActionKind.PlayNext, MenuActionKind.AddToQueue, MenuActionKind.InstantMix);
    }

    [Fact]
    public void Every_item_shows_its_translated_text_not_its_resource_key()
    {
        // A lookup that never returns the key itself, unlike the one above: a key passed on as text shows.
        var strings = Substitute.For<IStringResources>();
        strings.GetString(Arg.Any<string>()).Returns(call => $"«{call.Arg<string>()}»");
        var menus = new ContextMenus(_player, _library, _playlists, _navigation, _favourites, new InstantMix(_library, _player, new InMemorySettingsService(), _errors), _dialogs, _errors, strings);

        IEnumerable<MenuAction> all =
        [
            .. menus.ForTrack(Song),
            .. menus.ForTracks(AlbumTracks),
            .. menus.ForAlbum(new Album { Id = "al1", Name = "Discovery", AlbumArtists = [new ArtistRef("ar1", "Daft Punk")] }),
            .. menus.ForArtist(new Artist { Id = "ar1", Name = "Daft Punk" }),
            .. menus.ForPlaylist(new Playlist { Id = "p1", Name = "Chill" }),
            .. menus.ForGenre(new Genre("g1", "Electronic")),
            .. menus.ForFolder(new Folder("f1", "Daft Punk")),
        ];

        all.Should().OnlyContain(a => a.Text.StartsWith('«'), "each text comes from the resources");
        menus.ForTrack(Song).Single(a => a.Kind == MenuActionKind.GoToAlbum).Text.Should().Be("«Menu_GoToAlbum»");
    }
}
