using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Models;
using Groovefin.Core.Servers;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Downloads;

/// <summary>Albums, playlists and artists downloaded one by one, not as favourites (#387).</summary>
public sealed class DownloadChoicesTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly FakeDownloadSource _source = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly JsonDownloadStore _store;
    private readonly DownloadManager _sut;

    public DownloadChoicesTests()
    {
        _client.Connection.Returns(new ServerConnection(new Uri("http://media:8096"), "user1", "token"));
        _library.GetTrackFilesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, TrackFile>());

        // Discovery (a, b), Daft Punk (a, b, d) and Road trip (b, c); none of them a favourite.
        _library.GetAlbumAsync("al1", Arg.Any<CancellationToken>()).Returns(new Album { Id = "al1", Name = "Discovery" });
        _library.GetAlbumTracksAsync("al1", Arg.Any<CancellationToken>()).Returns([Song("a"), Song("b")]);
        _library.GetArtistAsync("ar1", Arg.Any<CancellationToken>()).Returns(new Artist { Id = "ar1", Name = "Daft Punk" });
        _library.GetArtistTracksAsync("ar1", Arg.Any<CancellationToken>()).Returns([Song("a"), Song("b"), Song("d")]);
        _playlists.GetPlaylistAsync("p1", Arg.Any<CancellationToken>()).Returns(new Playlist { Id = "p1", Name = "Road trip" });
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([new Playlist { Id = "p1", Name = "Road trip" }]);
        _playlists.GetEntriesAsync("p1", Arg.Any<CancellationToken>()).Returns([new PlaylistEntry("e1", Song("b")), new PlaylistEntry("e2", Song("c"))]);
        _store = new JsonDownloadStore(new DownloadStoreOptions(_directory) { FreeSpace = _ => 50L << 30 }, NullLogger<JsonDownloadStore>.Instance);
        _sut = new DownloadManager(
            _client, _library, _playlists, _source, _store, _settings, new FakeNetworkStatus(), Substitute.For<IServerManager>(),
            _messenger, new FakeTimeProvider(), NullLogger<DownloadManager>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Folder => _store.FolderOf(_client.Connection!.UserDataKey);

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Track Song(string id) => new() { Id = id, Name = $"Song {id}" };

    private string[] Files() => Directory.Exists(Folder)
        ? [.. Directory.EnumerateFiles(Folder).Select(Path.GetFileName).Where(n => n?.StartsWith(JsonDownloadStore.IndexFileName, StringComparison.Ordinal) == false).Order(StringComparer.Ordinal).OfType<string>()]
        : [];

    [Fact]
    public async Task Choosing_an_album_downloads_only_its_songs_without_a_favourite()
    {
        _sut.SetChosen(DownloadItemKind.Album, "al1", "Discovery", chosen: true);

        await _sut.SyncAsync();

        _sut.IsChosen(DownloadItemKind.Album, "al1").Should().BeTrue();
        Files().Should().Equal("a.flac", "b.flac");
        _sut.IsComplete("al1").Should().BeTrue("its downloaded mark shows");
        (await _store.LoadAsync(_client.Connection!.UserDataKey, Ct)).Albums.Should().ContainSingle().Which.Album.Name.Should().Be("Discovery");
        await _library.DidNotReceiveWithAnyArgs().SetFavouriteAsync(default!, default, Ct);
        await _library.DidNotReceiveWithAnyArgs().GetFavouriteTracksAsync(default, Ct);
        _settings.Current.Downloads.Should().BeEquivalentTo(new { FavouriteSongs = false, FavouriteAlbums = false, FavouriteArtists = false, Playlists = false });
        _settings.Current.Downloads.Chosen[_client.Connection.UserDataKey].Should().Equal(new DownloadChoice(DownloadItemKind.Album, "al1", "Discovery"));
    }

    [Theory]
    [InlineData(DownloadItemKind.Album, "al1", new[] { "a.flac", "b.flac" })]
    [InlineData(DownloadItemKind.Playlist, "p1", new[] { "b.flac", "c.flac" })]
    [InlineData(DownloadItemKind.Artist, "ar1", new[] { "a.flac", "b.flac", "d.flac" })]
    public async Task Each_kind_downloads_its_songs_and_turned_off_deletes_them(DownloadItemKind kind, string id, string[] files)
    {
        _sut.SetChosen(kind, id, "Item", chosen: true);
        await _sut.SyncAsync();
        Files().Should().Equal(files);

        _sut.SetChosen(kind, id, "Item", chosen: false);
        await _sut.SyncAsync();

        Files().Should().BeEmpty();
        _settings.Current.Downloads.Chosen.Should().BeEmpty();
    }

    [Fact]
    public async Task Turned_off_its_songs_stay_when_something_else_wants_them()
    {
        _sut.SetChosen(DownloadItemKind.Album, "al1", "Discovery", chosen: true);
        _sut.SetChosen(DownloadItemKind.Playlist, "p1", "Road trip", chosen: true);
        await _sut.SyncAsync();
        Files().Should().Equal("a.flac", "b.flac", "c.flac");

        _sut.SetChosen(DownloadItemKind.Album, "al1", "Discovery", chosen: false);
        await _sut.SyncAsync();

        Files().Should().Equal(new[] { "b.flac", "c.flac" }, "the playlist still wants b");
        _source.Count("b").Should().Be(1);
    }

    [Fact]
    public async Task A_favourite_switch_keeps_what_it_downloads_when_the_choice_goes()
    {
        _settings.Update(s => s with { Downloads = s.Downloads with { Playlists = true } });
        _sut.SetChosen(DownloadItemKind.Playlist, "p1", "Road trip", chosen: true);
        await _sut.SyncAsync();

        _sut.SetChosen(DownloadItemKind.Playlist, "p1", "Road trip", chosen: false);
        await _sut.SyncAsync();

        Files().Should().Equal("b.flac", "c.flac");
        await _playlists.Received().GetEntriesAsync("p1", Arg.Any<CancellationToken>());
        (await _store.LoadAsync(_client.Connection!.UserDataKey, Ct)).Playlists.Should().ContainSingle("a playlist both want is kept once");
    }

    [Fact]
    public async Task Choosing_what_was_removed_on_the_Downloads_page_downloads_it_again()
    {
        _settings.Update(s => s with { Downloads = s.Downloads with { Playlists = true } });
        await _sut.SyncAsync();
        _sut.Remove(_sut.GetItems().Single(i => i.Kind == DownloadItemKind.Playlist));
        await _sut.SyncAsync();
        Files().Should().BeEmpty();

        _sut.SetChosen(DownloadItemKind.Playlist, "p1", "Road trip", chosen: true);
        await _sut.SyncAsync();

        _sut.Removed.Should().BeEmpty();
        Files().Should().Equal("b.flac", "c.flac");
    }

    [Fact]
    public async Task Removing_a_chosen_album_on_the_Downloads_page_ends_the_choice()
    {
        _sut.SetChosen(DownloadItemKind.Album, "al1", "Discovery", chosen: true);
        await _sut.SyncAsync();

        _sut.Remove(_sut.GetItems().Single(i => i.Kind == DownloadItemKind.Album));
        await _sut.SyncAsync();

        _sut.IsChosen(DownloadItemKind.Album, "al1").Should().BeFalse();
        _sut.Removed.Should().BeEmpty("no switch downloads it: nothing to keep it from");
        Files().Should().BeEmpty();
    }

    [Fact]
    public async Task A_chosen_album_the_server_no_longer_has_is_left_out()
    {
        _library.GetAlbumAsync("gone", Arg.Any<CancellationToken>()).Returns<Album>(_ => throw new JellyfinException("Not found", HttpStatusCode.NotFound));
        _sut.SetChosen(DownloadItemKind.Album, "gone", "Old album", chosen: true);
        _sut.SetChosen(DownloadItemKind.Album, "al1", "Discovery", chosen: true);

        await _sut.SyncAsync();

        Files().Should().Equal("a.flac", "b.flac");
        _sut.Status.Stopped.Should().Be(DownloadStop.None);
        _sut.IsChosen(DownloadItemKind.Album, "gone").Should().BeTrue("it downloads should it come back");
    }

    [Fact]
    public void A_choice_belongs_to_its_server_and_user()
    {
        _sut.SetChosen(DownloadItemKind.Album, "al1", "Discovery", chosen: true);

        _client.Connection.Returns(new ServerConnection(new Uri("http://media:8096"), "user2", "token"));

        _sut.IsChosen(DownloadItemKind.Album, "al1").Should().BeFalse();
        _settings.Current.Downloads.AnyCollection.Should().BeTrue();
    }

    [Fact]
    public void The_page_toggle_follows_a_choice_from_a_menu()
    {
        var toggle = new DownloadToggle(_sut, DownloadItemKind.Album);
        toggle.CanChange.Should().BeFalse();
        toggle.Show("al1", "Discovery", _messenger);
        var changes = new List<string?>();
        toggle.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _sut.SetChosen(DownloadItemKind.Album, "al1", "Discovery", chosen: true);

        toggle.IsOn.Should().BeTrue();
        changes.Should().Contain(nameof(DownloadToggle.IsOn));

        toggle.IsOn = false;

        _sut.IsChosen(DownloadItemKind.Album, "al1").Should().BeFalse();
    }
}
