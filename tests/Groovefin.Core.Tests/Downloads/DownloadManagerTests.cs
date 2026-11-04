using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Favourites;
using Groovefin.Core.Models;
using Groovefin.Core.Servers;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Downloads;

public sealed class DownloadManagerTests : IDisposable
{
    private const string PlaylistId = "p1";

    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly FakeDownloadSource _source = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly FakeNetworkStatus _network = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeTimeProvider _time = new();
    private readonly JsonDownloadStore _store;
    private readonly DownloadManager _sut;
    private long _freeSpace = 50L << 30;

    public DownloadManagerTests()
    {
        _client.Connection.Returns(new ServerConnection(new Uri("http://media:8096"), "user1", "token"));
        Favourites("a", "b");
        Playlist("b", "c");
        _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteSongs = true, Playlists = true } });
        _store = new JsonDownloadStore(new DownloadStoreOptions(_directory) { FreeSpace = _ => _freeSpace }, NullLogger<JsonDownloadStore>.Instance);
        _sut = new DownloadManager(
            _client,
            _library,
            _playlists,
            _source,
            _store,
            _settings,
            _network,
            Substitute.For<IServerManager>(),
            _messenger,
            _time,
            NullLogger<DownloadManager>.Instance);
    }

    private string Folder => _store.FolderOf(_client.Connection!.UserDataKey);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Track Song(string id) => new() { Id = id, Name = $"Song {id}" };

    private void Favourites(params string[] ids) =>
        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([.. ids.Select(Song)]);

    private void Playlist(params string[] ids)
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([new Playlist { Id = PlaylistId, Name = "Road trip" }]);
        _playlists.GetEntriesAsync(PlaylistId, Arg.Any<CancellationToken>()).Returns([.. ids.Select(id => new PlaylistEntry($"e-{id}", Song(id)))]);
    }

    private string[] Files() => Directory.Exists(Folder)
        ? [.. Directory.EnumerateFiles(Folder).Select(Path.GetFileName).Where(n => n != JsonDownloadStore.IndexFileName).Order(StringComparer.Ordinal).OfType<string>()]
        : [];

    [Fact]
    public async Task Favourite_songs_and_playlist_songs_are_downloaded_once()
    {
        await _sut.SyncAsync();

        Files().Should().Equal("a.flac", "b.flac", "c.flac");
        _source.Count("b").Should().Be(1, "the song is in both collections");
        File.ReadAllBytes(Path.Join(Folder, "c.flac")).Should().Equal(_source.FileOf("c"));
        var index = await _store.LoadAsync(_client.Connection!.UserDataKey, Ct);
        index.Tracks.Keys.Should().BeEquivalentTo(["a", "b", "c"]);
        index.FavouriteSongs.Should().Equal("a", "b");
        index.Playlists.Should().ContainSingle().Which.TrackIds.Should().Equal("b", "c");
        _sut.Status.Should().BeEquivalentTo(new { Wanted = 3, Downloaded = 3, Bytes = 12L, IsSyncing = false, Stopped = DownloadStop.None });
    }

    [Fact]
    public async Task A_song_no_collection_wants_any_more_is_deleted_and_the_others_are_not_downloaded_again()
    {
        await _sut.SyncAsync();
        Favourites("b");

        await _sut.SyncAsync();

        Files().Should().Equal("b.flac", "c.flac");
        _source.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Turning_every_collection_off_deletes_the_downloads()
    {
        await _sut.SyncAsync();
        _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteSongs = false, Playlists = false } });

        await _sut.SyncAsync();

        Directory.Exists(Folder).Should().BeFalse();
        _sut.Status.Bytes.Should().Be(0);
    }

    [Fact]
    public async Task With_nothing_on_no_folder_is_made()
    {
        _settings.Update(s => s with { Downloads = new DownloadSettings() });

        await _sut.SyncAsync();

        Directory.Exists(_directory).Should().BeFalse();
        await _library.DidNotReceiveWithAnyArgs().GetFavouriteTracksAsync(default, Ct);
    }

    [Fact]
    public async Task Offline_nothing_is_read_or_downloaded()
    {
        _network.IsOnline = false;

        await _sut.SyncAsync();

        await _library.DidNotReceiveWithAnyArgs().GetFavouriteTracksAsync(default, Ct);
        _source.Requests.Should().BeEmpty();
        _sut.Status.Stopped.Should().Be(DownloadStop.Offline);
    }

    [Fact]
    public async Task A_metered_connection_waits_unless_downloads_there_are_allowed_this_time()
    {
        _network.IsMetered = true;

        await _sut.SyncAsync();
        _source.Requests.Should().BeEmpty();
        _sut.Status.Stopped.Should().Be(DownloadStop.Metered);

        await _sut.SyncNowAsync(allowMetered: true);
        Files().Should().HaveCount(3);
    }

    [Fact]
    public async Task Less_than_a_gigabyte_free_stops_the_downloads()
    {
        _freeSpace = 500L << 20;

        await _sut.SyncAsync();

        _source.Requests.Should().BeEmpty();
        _sut.Status.Stopped.Should().Be(DownloadStop.LowDisk);
    }

    [Fact]
    public async Task A_failing_song_is_tried_three_times_then_only_on_sync_now()
    {
        _source.Failures["a"] = new JellyfinException("Forbidden", HttpStatusCode.Forbidden);

        for (var i = 0; i < 4; i++)
        {
            await _sut.SyncAsync();
        }

        _source.Count("a").Should().Be(DownloadManager.MaxAttempts);
        _sut.Status.Failures.Should().ContainSingle().Which.Should().Be(new DownloadFailure("a", "Song a", "HTTP 403", 3));
        Files().Should().Equal("b.flac", "c.flac");

        await _sut.SyncNowAsync();
        _source.Count("a").Should().Be(DownloadManager.MaxAttempts + 1);
    }

    [Fact]
    public async Task An_unreachable_server_stops_the_sync_without_counting_a_failure()
    {
        _source.Failures["a"] = new JellyfinException("Unreachable", (HttpStatusCode?)null);
        _source.Failures["b"] = new JellyfinException("Unreachable", (HttpStatusCode?)null);
        _source.Failures["c"] = new JellyfinException("Unreachable", (HttpStatusCode?)null);

        await _sut.SyncAsync();

        _sut.Status.Stopped.Should().Be(DownloadStop.Unreachable);
        _sut.Status.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task An_interrupted_original_resumes_where_it_stopped()
    {
        Favourites("abcdefgh");
        Playlist();
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Path.Join(Folder, "abcdefgh.part"), _source.FileOf("abcdefgh")[..3], Ct);

        await _sut.SyncAsync();

        _source.Requests.Should().ContainSingle().Which.Offset.Should().Be(3);
        File.ReadAllBytes(Path.Join(Folder, "abcdefgh.flac")).Should().Equal(_source.FileOf("abcdefgh"));
    }

    [Fact]
    public async Task A_download_cut_short_keeps_its_part_and_counts_as_a_failure()
    {
        _source.CutShort.Add("a");

        await _sut.SyncAsync();

        Files().Should().Equal("a.part", "b.flac", "c.flac");
        _sut.Status.Failures.Should().ContainSingle().Which.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Another_format_downloads_every_song_again_and_replaces_its_file()
    {
        await _sut.SyncAsync();
        _settings.Update(s => s with { Downloads = s.Downloads with { Format = DownloadFormatKind.Aac, AacBitrateKbps = 192 } });

        await _sut.SyncAsync();

        Files().Should().Equal("a.aac", "b.aac", "c.aac");
        _source.Requests.Where(r => r.Format == "aac192").Should().HaveCount(3);
        (await _store.LoadAsync(_client.Connection!.UserDataKey, Ct)).Tracks.Values.Should().OnlyContain(t => t.Format == "aac192");
    }

    [Fact]
    public async Task Remove_all_deletes_every_download()
    {
        await _sut.SyncAsync();

        await _sut.RemoveAllAsync();

        Directory.Exists(Folder).Should().BeFalse();
        _sut.Status.Should().BeEquivalentTo(new { Wanted = 0, Downloaded = 0, Bytes = 0L });
    }

    [Fact]
    public async Task A_new_favourite_syncs_a_moment_later()
    {
        _messenger.Send(new FavouriteChangedMessage("a", true));
        _time.Advance(DownloadManager.ChangeDelay);

        // Until the sync has finished, after its last write: the files alone appear before the index is saved.
        var until = DateTime.UtcNow.AddSeconds(10);
        while ((_sut.Status.Downloaded < 3 || _sut.Status.IsSyncing) && DateTime.UtcNow < until)
        {
            await Task.Delay(50, Ct);
        }

        Files().Should().HaveCount(3);
    }

    [Fact]
    public async Task The_status_shows_what_is_on_disk_before_the_first_sync()
    {
        await _sut.SyncAsync();
        using var later = new DownloadManager(
            _client, _library, _playlists, _source, _store, _settings, _network, Substitute.For<IServerManager>(), new WeakReferenceMessenger(), _time, NullLogger<DownloadManager>.Instance);

        await later.RefreshStatusAsync();

        later.Status.Should().BeEquivalentTo(new { Wanted = 3, Downloaded = 3, Bytes = 12L });
    }
}
