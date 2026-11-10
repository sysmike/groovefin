using System.Net;
using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Core.Tests.Downloads;

/// <summary>The pages' library and playlist services answer from the downloads when the server cannot be reached (#335).</summary>
public sealed class OfflineServicesTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly ILibraryService _serverLibrary = Substitute.For<ILibraryService>();
    private readonly IPlaylistService _serverPlaylists = Substitute.For<IPlaylistService>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly FakeNetworkStatus _network = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly ISearchService _serverSearch = Substitute.For<ISearchService>();
    private readonly JsonDownloadStore _store;
    private readonly OfflineSwitch _switch;
    private readonly OfflineMode _mode;
    private readonly OfflineLibraryService _library;
    private readonly OfflinePlaylistService _playlists;
    private readonly OfflineSearchService _search;

    public OfflineServicesTests()
    {
        _client.Connection.Returns(new ServerConnection(new Uri("http://media:8096/"), "user1", "token"));
        _store = new JsonDownloadStore(new DownloadStoreOptions(_directory), NullLogger<JsonDownloadStore>.Instance);
        var strings = Substitute.For<IStringResources>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _switch = new OfflineSwitch(_network, _settings);
        _mode = new OfflineMode(_switch, _notifications, strings, new ImmediateDispatcher());
        var answers = new OfflineAnswers(new OfflineCatalog(_client, _store), _mode, _switch);
        _library = new OfflineLibraryService(_serverLibrary, answers);
        _playlists = new OfflinePlaylistService(_serverPlaylists, answers);
        _search = new OfflineSearchService(_serverSearch, answers);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JellyfinException Unreachable => new("Unreachable", (HttpStatusCode?)null);

    public void Dispose()
    {
        _mode.Dispose();
        _switch.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Track Song(string id, string album = "al1", int number = 1, string artist = "ar1") =>
        new()
        {
            Id = id,
            Name = $"Song {id}",
            AlbumId = album,
            AlbumName = "Discovery",
            TrackNumber = number,
            Artists = [new ArtistRef(artist, "Daft Punk")],
            Genres = [new Genre("g1", "Electronic")],
            Duration = TimeSpan.FromMinutes(1),
        };

    /// <summary>Downloads d1 and d2 (album al1, artist ar1); the favourites and the playlist also want w, which is not downloaded.</summary>
    private async Task DownloadedAsync()
    {
        var index = new DownloadIndex
        {
            FavouriteSongs = ["d2", "w"],
            Playlists = [new DownloadedPlaylist { Playlist = new Playlist { Id = "p1", Name = "Road trip", TrackCount = 3 }, TrackIds = ["d2", "w", "d1"] }],
            Albums = [new DownloadedAlbum { Album = new Album { Id = "al1", Name = "Discovery", IsFavorite = true }, TrackIds = ["d1", "d2"] }],
            Artists = [new DownloadedArtist { Artist = new Artist { Id = "ar1", Name = "Daft Punk", IsFavorite = true }, TrackIds = ["d1", "d2"] }],
        };
        index.Tracks["d1"] = new DownloadedTrack { Track = Song("d1", number: 1), FileName = "d1.flac", Format = "original" };
        index.Tracks["d2"] = new DownloadedTrack { Track = Song("d2", number: 2), FileName = "d2.flac", Format = "original" };
        await _store.SaveAsync(_client.Connection!.UserDataKey, index, Ct);
    }

    [Fact]
    public async Task Online_the_server_answers_and_no_notice_shows()
    {
        await DownloadedAsync();
        _serverLibrary.GetFavouriteTracksAsync(100, Arg.Any<CancellationToken>()).Returns([Song("s")]);

        var songs = await _library.GetFavouriteTracksAsync(100, Ct);

        songs.Select(t => t.Id).Should().Equal("s");
        _mode.IsOffline.Should().BeFalse();
        _notifications.DidNotReceiveWithAnyArgs().Show(default!);
    }

    [Fact]
    public async Task An_unreachable_server_gives_the_downloaded_favourites_and_shows_the_notice()
    {
        await DownloadedAsync();
        _serverLibrary.GetFavouriteTracksAsync(100, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Track>>(_ => throw Unreachable);
        _serverLibrary.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>()).Returns<PagedResult<Album>>(_ => throw Unreachable);
        _serverLibrary.GetArtistsAsync(Arg.Any<ArtistQuery>(), Arg.Any<CancellationToken>()).Returns<PagedResult<Artist>>(_ => throw Unreachable);

        var songs = await _library.GetFavouriteTracksAsync(100, Ct);
        var albums = await _library.GetAlbumsAsync(new AlbumQuery { FavouritesOnly = true }, Ct);
        var artists = await _library.GetArtistsAsync(new ArtistQuery { FavouritesOnly = true, AlbumArtistsOnly = false }, Ct);

        songs.Select(t => t.Id).Should().Equal(new[] { "d2" }, "w is not downloaded");
        albums.Items.Select(a => a.Id).Should().Equal("al1");
        artists.Items.Select(a => a.Id).Should().Equal("ar1");
        _mode.IsOffline.Should().BeTrue();
        _notifications.Received(1).Show(Arg.Is<AppNotification>(n => n.Title == "Offline_Title" && n.Severity == NotificationSeverity.Informational));
    }

    [Fact]
    public async Task Without_a_network_the_server_is_not_asked()
    {
        await DownloadedAsync();
        _network.IsOnline = false;

        var tracks = await _library.GetAlbumTracksAsync("al1", Ct);

        tracks.Select(t => t.Id).Should().Equal("d1", "d2");
        await _serverLibrary.DidNotReceiveWithAnyArgs().GetAlbumTracksAsync(default!, Ct);
    }

    [Fact]
    public async Task An_album_and_an_artist_page_get_their_downloaded_songs()
    {
        await DownloadedAsync();
        _network.IsOnline = false;

        (await _library.GetAlbumAsync("al1", Ct)).Name.Should().Be("Discovery");
        (await _library.GetArtistAsync("ar1", Ct)).Name.Should().Be("Daft Punk");
        (await _library.GetArtistTracksAsync("ar1", Ct)).Select(t => t.Id).Should().Equal("d1", "d2");
        (await _library.GetArtistTopTracksAsync("ar1", 1, Ct)).Should().ContainSingle();
        (await _library.GetAlbumsAsync(new AlbumQuery { AlbumArtistId = "ar1" }, Ct)).Items.Select(a => a.Id).Should().Equal("al1");
        (await _library.GetSimilarArtistsAsync("ar1", 16, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task What_the_downloads_do_not_hold_fails_as_before()
    {
        await DownloadedAsync();
        _serverLibrary.GetAlbumAsync("other", Arg.Any<CancellationToken>()).Returns<Album>(_ => throw Unreachable);

        var act = () => _library.GetAlbumAsync("other", Ct);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task Another_server_error_is_not_answered_from_the_downloads()
    {
        await DownloadedAsync();
        _serverLibrary.GetFavouriteTracksAsync(100, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Track>>(_ => throw new JellyfinException("Forbidden", HttpStatusCode.Forbidden));

        var act = () => _library.GetFavouriteTracksAsync(100, Ct);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _mode.IsOffline.Should().BeFalse();
    }

    [Fact]
    public async Task Offline_every_section_shows_the_downloads()
    {
        await DownloadedAsync();
        _network.IsOnline = false;

        (await _library.GetAlbumsAsync(new AlbumQuery(), Ct)).Items.Select(a => a.Id).Should().Equal("al1");
        (await _library.GetArtistsAsync(new ArtistQuery(), Ct)).Items.Select(a => a.Id).Should().Equal("ar1");
        (await _library.GetTracksAsync(new TrackQuery(), Ct)).Items.Select(t => t.Id).Should().Equal("d1", "d2");
        (await _library.GetAlbumGenresAsync(Ct)).Should().ContainSingle().Which.Name.Should().Be("Electronic");
        (await _library.GetGenreTracksAsync("g1", 100, Ct)).Should().HaveCount(2);
        (await _search.SearchAsync("song d1", 10, Ct)).Tracks.Select(t => t.Id).Should().Equal("d1");
        await _serverLibrary.DidNotReceiveWithAnyArgs().GetAlbumsAsync(default!, Ct);
        await _serverSearch.DidNotReceiveWithAnyArgs().SearchAsync(default!, default, Ct);
    }

    [Fact]
    public async Task Downloaded_only_is_answered_from_the_downloads_online_too()
    {
        await DownloadedAsync();

        var albums = await _library.GetAlbumsAsync(new AlbumQuery { DownloadedOnly = true }, Ct);
        var songs = await _library.GetTracksAsync(new TrackQuery { DownloadedOnly = true, Limit = 1 }, Ct);

        albums.Items.Select(a => a.Id).Should().Equal("al1");
        songs.Items.Select(t => t.Id).Should().Equal("d1");
        songs.TotalCount.Should().Be(2);
        await _serverLibrary.DidNotReceiveWithAnyArgs().GetAlbumsAsync(default!, Ct);
        await _serverLibrary.DidNotReceiveWithAnyArgs().GetTracksAsync(default!, Ct);
        _mode.IsOffline.Should().BeFalse("the server can be reached");
    }

    [Fact]
    public async Task Downloaded_only_without_downloads_is_empty()
    {
        var albums = await _library.GetAlbumsAsync(new AlbumQuery { DownloadedOnly = true }, Ct);

        albums.Items.Should().BeEmpty();
        albums.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Working_offline_the_server_is_not_asked_and_its_notice_shows()
    {
        await DownloadedAsync();

        _switch.IsOn = true;
        var albums = await _library.GetAlbumsAsync(new AlbumQuery(), Ct);

        _settings.Current.Downloads.WorkOffline.Should().BeTrue("it is saved");
        _switch.IsOnline.Should().BeFalse();
        albums.Items.Select(a => a.Id).Should().Equal("al1");
        await _serverLibrary.DidNotReceiveWithAnyArgs().GetAlbumsAsync(default!, Ct);
        _notifications.Received(1).Show(Arg.Is<AppNotification>(n => n.Title == "WorkOffline_Title"));
        _notifications.DidNotReceive().Show(Arg.Is<AppNotification>(n => n.Title == "Offline_Title"));

        _switch.ToggleCommand.Execute(null);
        _serverLibrary.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<Album>([], 0, 0));
        await _library.GetAlbumsAsync(new AlbumQuery(), Ct);

        _switch.IsOnline.Should().BeTrue();
        await _serverLibrary.ReceivedWithAnyArgs(1).GetAlbumsAsync(default!, Ct);
        _notifications.Received(1).Dismiss(Arg.Is<AppNotification>(n => n.Title == "WorkOffline_Title"));
    }

    [Fact]
    public void The_switch_raises_its_change_and_follows_the_network()
    {
        var changes = 0;
        _switch.Changed += (_, _) => changes++;

        _switch.IsOn = true;
        _switch.IsOn = true;
        _network.IsOnline = false;
        _switch.IsOn = false;

        changes.Should().Be(3, "it was turned on, the network went, and it was turned off");
        _switch.IsOnline.Should().BeFalse("the PC has no network");
        _network.IsOnline = true;
        _switch.IsOnline.Should().BeTrue();
    }

    [Fact]
    public void A_switch_saved_on_shows_its_notice_at_start()
    {
        var settings = new InMemorySettingsService();
        settings.Update(s => s with { Downloads = s.Downloads with { WorkOffline = true } });
        var notifications = Substitute.For<INotificationService>();
        var strings = Substitute.For<IStringResources>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        using var offline = new OfflineSwitch(_network, settings);

        using var mode = new OfflineMode(offline, notifications, strings, new ImmediateDispatcher());

        offline.IsOnline.Should().BeFalse();
        notifications.Received(1).Show(Arg.Is<AppNotification>(n => n.Title == "WorkOffline_Title"));
    }

    [Fact]
    public async Task Offline_playlists_hold_their_downloaded_songs_with_their_count_and_length()
    {
        await DownloadedAsync();
        _network.IsOnline = false;

        var playlist = (await _playlists.GetPlaylistsAsync(Ct)).Should().ContainSingle().Subject;
        var entries = await _playlists.GetEntriesAsync("p1", Ct);

        playlist.Should().BeEquivalentTo(new { Id = "p1", TrackCount = 2, Duration = TimeSpan.FromMinutes(2) });
        (await _playlists.GetPlaylistAsync("p1", Ct)).TrackCount.Should().Be(2);
        entries.Select(e => e.Track.Id).Should().Equal("d2", "d1");
        entries.Select(e => e.EntryId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task The_notice_goes_when_the_server_answers_again()
    {
        await DownloadedAsync();
        _serverLibrary.GetFavouriteTracksAsync(100, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Track>>(_ => throw Unreachable, _ => [Song("s")]);
        await _library.GetFavouriteTracksAsync(100, Ct);

        await _library.GetFavouriteTracksAsync(100, Ct);

        _mode.IsOffline.Should().BeFalse();
        _notifications.Received(1).Dismiss(Arg.Is<AppNotification>(n => n.Title == "Offline_Title"));
    }
}
