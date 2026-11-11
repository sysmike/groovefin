using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Favourites;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Servers;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ReturnsExtensions;

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
    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly Dictionary<string, TrackFile> _files = [];
    private readonly JsonDownloadStore _store;
    private readonly DownloadManager _sut;
    private long _freeSpace = 50L << 30;
    private bool _driveReady = true;
    private readonly string _chosen = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));

    public DownloadManagerTests()
    {
        _client.Connection.Returns(new ServerConnection(new Uri("http://media:8096"), "user1", "token"));
        Favourites("a", "b");
        Playlist("b", "c");
        _library.GetTrackFilesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyDictionary<string, TrackFile>)call.Arg<IReadOnlyList<string>>()
                .Where(_files.ContainsKey).ToDictionary(id => id, id => _files[id]));
        _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteSongs = true, Playlists = true } });
        _store = new JsonDownloadStore(
            new DownloadStoreOptions(_directory) { FreeSpace = _ => _freeSpace, DriveReady = _ => _driveReady },
            NullLogger<JsonDownloadStore>.Instance);
        _sut = Manager(_store);
    }

    /// <summary>Another manager on the same folder: what the app is after a restart.</summary>
    private DownloadManager Manager(IDownloadStore store) => new(
        _client,
        _library,
        _playlists,
        _source,
        store,
        _settings,
        _network,
        _servers,
        _messenger,
        _time,
        NullLogger<DownloadManager>.Instance);

    private string IndexPath => Path.Join(Folder, JsonDownloadStore.IndexFileName);

    /// <summary>Gives the songs their file on the server, as long as the fake source's.</summary>
    private void ServerFiles(params string[] ids)
    {
        foreach (var id in ids)
        {
            _files[id] = new TrackFile("flac", _source.FileOf(id).Length);
        }
    }

    /// <summary>Counts the saves of the index.</summary>
    private sealed class CountingStore(IDownloadStore inner) : IDownloadStore
    {
        public int Saves { get; private set; }

        public string Root => inner.Root;

        public bool IsAvailable => inner.IsAvailable;

        public string RootOf(string? chosenFolder) => inner.RootOf(chosenFolder);

        public Task<DownloadMoveResult> MoveAsync(string root, IProgress<DownloadMoveProgress>? progress, CancellationToken cancellationToken) =>
            inner.MoveAsync(root, progress, cancellationToken);

        public void RemoveLeftovers() => inner.RemoveLeftovers();

        public string FolderOf(string key) => inner.FolderOf(key);

        public long FreeSpace(string key) => inner.FreeSpace(key);

        public Task<DownloadIndex> LoadAsync(string key, CancellationToken cancellationToken = default) => inner.LoadAsync(key, cancellationToken);

        public Task SaveAsync(string key, DownloadIndex index, CancellationToken cancellationToken = default)
        {
            Saves++;
            return inner.SaveAsync(key, index, cancellationToken);
        }

        public void DeleteAll(string key) => inner.DeleteAll(key);

        public void Rename(string fromKey, string toKey) => inner.Rename(fromKey, toKey);

        public IReadOnlyList<string> Keys() => inner.Keys();

        public DateTime? IndexSavedAt(string key) => inner.IndexSavedAt(key);

        public string CoversOf(string key) => inner.CoversOf(key);
    }

    private string Folder => _store.FolderOf(_client.Connection!.UserDataKey);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _sut.Dispose();
        foreach (var folder in new[] { _directory, _chosen }.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
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
        ? [.. Directory.EnumerateFiles(Folder).Select(Path.GetFileName).Where(n => n?.StartsWith(JsonDownloadStore.IndexFileName, StringComparison.Ordinal) == false).Order(StringComparer.Ordinal).OfType<string>()]
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
        _source.CutShort.Add("abcdefgh");
        await _sut.SyncAsync();
        _source.CutShort.Clear();

        await _sut.SyncAsync();

        _source.Requests.Select(r => r.Offset).Should().Equal(0, 2);
        File.ReadAllBytes(Path.Join(Folder, "abcdefgh.flac")).Should().Equal(_source.FileOf("abcdefgh"));
        Files().Should().Equal("abcdefgh.flac");
    }

    [Fact]
    public async Task A_download_cut_short_keeps_its_part_and_counts_as_a_failure()
    {
        _source.CutShort.Add("a");

        await _sut.SyncAsync();

        Files().Should().HaveCount(3).And.Contain(["b.flac", "c.flac"]).And.ContainSingle(f => f.StartsWith("a.", StringComparison.Ordinal) && f.EndsWith(".part", StringComparison.Ordinal));
        _sut.Status.Failures.Should().ContainSingle().Which.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task A_part_file_of_unknown_origin_is_not_resumed()
    {
        // As versions before #427 named it: nothing tells what it was started from.
        Favourites("abcdefgh");
        Playlist();
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Path.Join(Folder, "abcdefgh.part"), [9, 9, 9], Ct);

        await _sut.SyncAsync();

        _source.Requests.Should().ContainSingle().Which.Offset.Should().Be(0);
        File.ReadAllBytes(Path.Join(Folder, "abcdefgh.flac")).Should().Equal(_source.FileOf("abcdefgh"));
        Files().Should().Equal("abcdefgh.flac");
    }

    [Fact]
    public async Task A_part_file_of_another_format_is_not_resumed()
    {
        Favourites("abcdefgh");
        Playlist();
        _settings.Update(s => s with { Downloads = s.Downloads with { Format = DownloadFormatKind.Aac, AacBitrateKbps = 256 } });
        _source.CutShort.Add("abcdefgh");
        await _sut.SyncAsync();
        _source.CutShort.Clear();
        _settings.Update(s => s with { Downloads = s.Downloads with { Format = DownloadFormatKind.Original } });

        await _sut.SyncAsync();

        _source.Requests.Should().OnlyContain(r => r.Offset == 0);
        File.ReadAllBytes(Path.Join(Folder, "abcdefgh.flac")).Should().Equal(_source.FileOf("abcdefgh"));
        Files().Should().Equal("abcdefgh.flac");
    }

    [Fact]
    public async Task A_part_file_of_a_file_the_server_replaced_is_not_resumed()
    {
        Favourites("a");
        Playlist();
        _source.Files["a"] = [1, 2, 3, 4];
        ServerFiles("a");
        _source.CutShort.Add("a");
        await _sut.SyncAsync();
        _source.CutShort.Clear();
        _source.Files["a"] = [5, 6, 7, 8, 9, 10];
        ServerFiles("a");

        await _sut.SyncAsync();

        _source.Requests.Should().OnlyContain(r => r.Offset == 0);
        File.ReadAllBytes(Path.Join(Folder, "a.flac")).Should().Equal(5, 6, 7, 8, 9, 10);
        Files().Should().Equal("a.flac");
    }

    [Fact]
    public async Task A_part_file_the_server_has_nothing_more_for_starts_over()
    {
        // HTTP 416: the part file is complete (its rename failed), or not of this file.
        Favourites("a");
        Playlist();
        _source.CutShort.Add("a");
        await _sut.SyncAsync();
        _source.CutShort.Clear();
        _source.RangeNotSatisfiable.Add("a");

        await _sut.SyncAsync();

        _source.Requests.Select(r => r.Offset).Should().Equal(0, 2, 0);
        File.ReadAllBytes(Path.Join(Folder, "a.flac")).Should().Equal(_source.FileOf("a"));
        Files().Should().Equal("a.flac");
        _sut.Status.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task A_long_sync_does_not_save_the_index_after_every_song()
    {
        var ids = Enumerable.Range(0, 60).Select(i => $"s{i:00}").ToArray();
        Favourites(ids);
        Playlist();
        var store = new CountingStore(_store);
        using var sut = Manager(store);

        await sut.SyncAsync();

        store.Saves.Should().BeLessThanOrEqualTo(2 + (ids.Length / DownloadManager.SaveEverySongs));
        (await _store.LoadAsync(_client.Connection!.UserDataKey, Ct)).Tracks.Keys.Should().BeEquivalentTo(ids);
    }

    [Fact]
    public async Task A_sync_that_is_stopped_saves_what_it_downloaded()
    {
        _source.Failures["c"] = new JellyfinException("No route to host.");

        await _sut.SyncAsync();

        _sut.Status.Stopped.Should().Be(DownloadStop.Unreachable);
        (await _store.LoadAsync(_client.Connection!.UserDataKey, Ct)).Tracks.Keys.Should().Contain("a");
    }

    [Fact]
    public async Task After_a_lost_index_the_files_that_are_the_servers_are_kept()
    {
        ServerFiles("a", "b", "c");
        await _sut.SyncAsync();
        _sut.Dispose();
        File.Delete(IndexPath);
        File.Delete(IndexPath + ".bak");
        await File.WriteAllBytesAsync(Path.Join(Folder, "c.flac"), [1], Ct);
        using var sut = Manager(_store);

        await sut.SyncAsync();

        _source.Count("a").Should().Be(1);
        _source.Count("b").Should().Be(1);
        _source.Count("c").Should().Be(2, "its file is not as long as the server's");
        var index = await _store.LoadAsync(_client.Connection!.UserDataKey, Ct);
        index.Tracks.Keys.Should().BeEquivalentTo(["a", "b", "c"]);
        index.Tracks["a"].Should().BeEquivalentTo(new { FileName = "a.flac", Size = 4L, Format = "original", FileVersion = "flac:4" });
    }

    [Fact]
    public async Task An_index_that_cannot_be_read_stops_the_sync_and_deletes_nothing()
    {
        await _sut.SyncAsync();
        _sut.Dispose();
        var before = await File.ReadAllBytesAsync(IndexPath, Ct);
        Favourites();
        Playlist();

        // In use before the app starts: its first read of the index fails too.
        await using (new FileStream(IndexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using var sut = Manager(_store);
            await sut.RefreshStatusAsync();
            await sut.SyncAsync();
            sut.FileOf("a").Should().BeNull();
        }

        Files().Should().Equal("a.flac", "b.flac", "c.flac");
        (await File.ReadAllBytesAsync(IndexPath, Ct)).Should().Equal(before);
        _source.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task An_index_of_a_newer_version_stops_the_sync_and_stays_as_it_is()
    {
        await _sut.SyncAsync();
        _sut.Dispose();
        var newer = (await File.ReadAllTextAsync(IndexPath, Ct)).Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal);
        await File.WriteAllTextAsync(IndexPath, newer, Ct);
        Favourites();
        Playlist();
        using var sut = Manager(_store);

        await sut.SyncAsync();

        Files().Should().Equal("a.flac", "b.flac", "c.flac");
        (await File.ReadAllTextAsync(IndexPath, Ct)).Should().Be(newer).And.Contain("\"version\":2");
    }

    /// <summary>An MP4 file's start: a box of 24 bytes, of type <c>ftyp</c>.</summary>
    private static byte[] Mp4() => [0, 0, 0, 24, .. "ftypM4A "u8, 0, 0, 0, 0, .. "M4A mp42"u8];

    [Fact]
    public async Task An_aac_download_that_is_an_mp4_file_is_named_m4a()
    {
        _settings.Update(s => s with { Downloads = s.Downloads with { Format = DownloadFormatKind.Aac, AacBitrateKbps = 192 } });
        _source.Files["a"] = Mp4();

        await _sut.SyncAsync();

        Files().Should().Equal("a.m4a", "b.aac", "c.aac");
        _sut.FileOf("a").Should().Be(Path.Join(Folder, "a.m4a"));
    }

    [Fact]
    public async Task An_mp4_file_named_aac_before_the_fix_is_renamed_without_downloading_it_again()
    {
        _settings.Update(s => s with { Downloads = s.Downloads with { Format = DownloadFormatKind.Aac, AacBitrateKbps = 192 } });
        await _sut.SyncAsync();
        File.WriteAllBytes(Path.Join(Folder, "a.aac"), Mp4());
        var index = await _store.LoadAsync(_client.Connection!.UserDataKey, Ct);
        index.AacFilesChecked = false;
        await _store.SaveAsync(_client.Connection!.UserDataKey, index, Ct);
        using var later = new DownloadManager(
            _client, _library, _playlists, _source, _store, _settings, _network, _servers, _messenger, _time, NullLogger<DownloadManager>.Instance);

        await later.SyncAsync();

        Files().Should().Equal("a.m4a", "b.aac", "c.aac");
        _source.Count("a").Should().Be(1);
        (await _store.LoadAsync(_client.Connection!.UserDataKey, Ct)).AacFilesChecked.Should().BeTrue();
    }

    [Theory]
    [InlineData("flac", true)]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", true)]
    [InlineData("opus", false)]
    [InlineData("ogg", false)]
    [InlineData("ape", false)]
    public void An_original_windows_cannot_play_comes_as_aac(string container, bool plays)
    {
        var format = new DownloadFormat(DownloadFormatKind.Original).For(new TrackFile(container, 1));

        format.Should().Be(plays ? new DownloadFormat(DownloadFormatKind.Original) : new DownloadFormat(DownloadFormatKind.Aac, DownloadFormat.ConvertedBitrateKbps));
    }

    [Fact]
    public async Task An_original_windows_cannot_play_is_downloaded_as_aac_once()
    {
        _files["a"] = new TrackFile("opus", 4);

        await _sut.SyncAsync();
        await _sut.SyncAsync();

        Files().Should().Equal("a.aac", "b.flac", "c.flac");
        _source.Requests.Where(r => r.TrackId == "a").Select(r => r.Format).Should().Equal($"aac{DownloadFormat.ConvertedBitrateKbps}");
        (await _store.LoadAsync(_client.Connection!.UserDataKey, Ct)).Tracks["a"].Format.Should().Be("original", "the chosen format counts");
    }

    [Fact]
    public async Task An_original_windows_cannot_play_kept_before_the_fix_is_downloaded_again()
    {
        _source.Extension = ".opus";
        await _sut.SyncAsync();
        _source.Extension = ".flac";
        _files["a"] = new TrackFile("opus", 4);

        await _sut.SyncAsync();

        Files().Should().Equal("a.aac", "b.opus", "c.opus");
        _source.Count("a").Should().Be(2);
        _source.Count("b").Should().Be(1);
    }

    [Fact]
    public async Task A_downloaded_flac_file_whose_audio_starts_far_in_has_it_moved_to_the_front()
    {
        _source.Files["a"] = FlacFile.WithCover((int)FlacMetadata.MaxAudioOffset);
        _source.Files["b"] = FlacFile.WithCover(100);

        await _sut.SyncAsync();

        var file = File.ReadAllBytes(Path.Join(Folder, "a.flac"));
        FlacFile.Blocks(file).Select(b => b.Type).Should().Equal(FlacFile.StreamInfo, FlacFile.Tags);
        File.ReadAllBytes(Path.Join(Folder, "b.flac")).Should().Equal(_source.Files["b"], "its audio starts soon");
        var index = await _store.LoadAsync(_client.Connection!.UserDataKey, Ct);
        index.Tracks["a"].Size.Should().Be(file.Length);
        index.FlacFilesCompacted.Should().BeTrue();
    }

    [Fact]
    public async Task Flac_files_downloaded_before_have_their_audio_moved_to_the_front_once()
    {
        await _sut.SyncAsync();
        File.WriteAllBytes(Path.Join(Folder, "a.flac"), FlacFile.WithCover((int)FlacMetadata.MaxAudioOffset));
        var index = await _store.LoadAsync(_client.Connection!.UserDataKey, Ct);
        index.FlacFilesCompacted = false;
        await _store.SaveAsync(_client.Connection!.UserDataKey, index, Ct);
        using var later = new DownloadManager(
            _client, _library, _playlists, _source, _store, _settings, _network, _servers, _messenger, _time, NullLogger<DownloadManager>.Instance);

        await later.SyncAsync();

        var file = File.ReadAllBytes(Path.Join(Folder, "a.flac"));
        FlacFile.Blocks(file).Select(b => b.Type).Should().Equal(FlacFile.StreamInfo, FlacFile.Tags);
        _source.Count("a").Should().Be(1, "the file is rewritten where it is");
        index = await _store.LoadAsync(_client.Connection!.UserDataKey, Ct);
        index.Tracks["a"].Size.Should().Be(file.Length);
        index.FlacFilesCompacted.Should().BeTrue();
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
    public async Task A_song_whose_file_changed_on_the_server_is_downloaded_again()
    {
        _files["a"] = new TrackFile("flac", 4);
        await _sut.SyncAsync();
        _files["a"] = new TrackFile("flac", 5);

        await _sut.SyncAsync();

        _source.Count("a").Should().Be(2);
        _source.Count("b").Should().Be(1);
        (await _store.LoadAsync(_client.Connection!.UserDataKey, Ct)).Tracks["a"].FileVersion.Should().Be("flac:5");
    }

    [Fact]
    public async Task A_download_from_before_the_marks_takes_the_servers_without_downloading_again()
    {
        await _sut.SyncAsync();
        _files["a"] = new TrackFile("flac", 4);

        await _sut.SyncAsync();
        await _sut.SyncAsync();

        _source.Count("a").Should().Be(1);
        (await _store.LoadAsync(_client.Connection!.UserDataKey, Ct)).Tracks["a"].FileVersion.Should().Be("flac:4");
    }

    [Fact]
    public async Task The_downloads_of_a_server_that_left_the_list_are_deleted()
    {
        await _sut.SyncAsync();
        var other = $"user1-{ServerConnection.ServerKeyOf(new Uri("https://old.example.com/"))}";
        Directory.CreateDirectory(_store.FolderOf(other));
        _servers.Servers.Returns([new ServerProfile { Id = "s1", Name = "Media", Address = new Uri("http://media:8096") }]);

        _servers.Changed += Raise.Event();

        var until = DateTime.UtcNow.AddSeconds(10);
        while (Directory.Exists(_store.FolderOf(other)) && DateTime.UtcNow < until)
        {
            await Task.Delay(50, Ct);
        }

        Directory.Exists(_store.FolderOf(other)).Should().BeFalse();
        Files().Should().HaveCount(3, "the active server stays in the list");
    }

    [Fact]
    public async Task Downloads_named_after_the_servers_address_are_kept_under_its_id()
    {
        // As versions before #429 kept them.
        await _sut.SyncAsync();
        _sut.Dispose();
        var legacy = _client.Connection!.UserDataKey;
        _settings.Update(s => s with
        {
            Downloads = s.Downloads with
            {
                Chosen = new Dictionary<string, IReadOnlyList<DownloadChoice>> { [legacy] = [new DownloadChoice(DownloadItemKind.Playlist, PlaylistId, "Road trip")] },
            },
        });
        var connection = new ServerConnection(new Uri("http://media:8096"), "user1", "token") { ServerId = "s1" };
        _client.Connection.Returns(connection);
        _servers.Servers.Returns([new ServerProfile { Id = "s1", Name = "Media", Address = connection.ServerUrl, UserId = "user1" }]);
        using var sut = Manager(_store);

        await sut.SyncAsync();

        connection.UserDataKey.Should().NotBe(legacy);
        Directory.Exists(_store.FolderOf(legacy)).Should().BeFalse();
        Files().Should().Equal("a.flac", "b.flac", "c.flac");
        _source.Requests.Should().HaveCount(3, "nothing downloads again");
        _settings.Current.Downloads.Chosen.Keys.Should().Equal(connection.UserDataKey);
    }

    [Fact]
    public async Task A_server_at_another_address_keeps_its_downloads()
    {
        var connection = new ServerConnection(new Uri("http://media:8096"), "user1", "token") { ServerId = "s1" };
        _client.Connection.Returns(connection);
        await _sut.SyncAsync();
        var moved = connection with { ServerUrl = new Uri("https://media.example.com/") };
        _client.Connection.Returns(moved);
        _servers.Servers.Returns([new ServerProfile { Id = "s1", Name = "Media", Address = moved.ServerUrl, UserId = "user1" }]);

        _servers.Changed += Raise.Event();
        await _sut.SyncAsync();

        moved.UserDataKey.Should().Be(connection.UserDataKey);
        Files().Should().Equal("a.flac", "b.flac", "c.flac");
        _source.Requests.Should().HaveCount(3, "nothing downloads again");
        _sut.FileOf("a").Should().NotBeNull();
    }

    [Fact]
    public async Task A_sync_that_fails_unexpectedly_does_not_stop_the_next_one()
    {
        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Track>>(_ => throw new InvalidOperationException("Not prepared for this."));
        await _sut.Awaiting(s => s.SyncAsync()).Should().NotThrowAsync();
        Favourites("a", "b");

        await _sut.SyncAsync();

        Files().Should().Equal("a.flac", "b.flac", "c.flac");
        _sut.Status.IsSyncing.Should().BeFalse();
    }

    [Fact]
    public async Task A_status_subscriber_that_fails_does_not_stop_the_syncs()
    {
        void Fail(object? sender, EventArgs e) => throw new InvalidOperationException("A broken subscriber.");
        _sut.StatusChanged += Fail;
        await _sut.Awaiting(s => s.SyncAsync()).Should().NotThrowAsync();
        _sut.StatusChanged -= Fail;
        Favourites("a", "b", "d");

        await _sut.SyncAsync();

        Files().Should().Contain("d.flac");
    }

    [Fact]
    public async Task Every_song_of_the_favourite_albums_and_artists_is_downloaded()
    {
        _settings.Update(s => s with { Downloads = new DownloadSettings { FavouriteAlbums = true, FavouriteArtists = true } });
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.FavouritesOnly), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Album>([new Album { Id = "al1", Name = "Discovery" }], 1, 0));
        _library.GetAlbumTracksAsync("al1", Arg.Any<CancellationToken>()).Returns([Song("d1"), Song("d2")]);
        _library.GetArtistsAsync(Arg.Is<ArtistQuery>(q => q.FavouritesOnly && !q.AlbumArtistsOnly), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Artist>([new Artist { Id = "ar1", Name = "Daft Punk" }], 1, 0));
        _library.GetArtistTracksAsync("ar1", Arg.Any<CancellationToken>()).Returns([Song("d2"), Song("h1")]);

        await _sut.SyncAsync();

        Files().Should().Equal("d1.flac", "d2.flac", "h1.flac");
        var index = await _store.LoadAsync(_client.Connection!.UserDataKey, Ct);
        index.Albums.Should().ContainSingle().Which.TrackIds.Should().Equal("d1", "d2");
        index.Artists.Should().ContainSingle().Which.TrackIds.Should().Equal("d2", "h1");
        _sut.Status.Wanted.Should().Be(3);
    }

    [Fact]
    public async Task A_heart_on_an_album_syncs_when_favourite_albums_are_on()
    {
        _settings.Update(s => s with { Downloads = new DownloadSettings { FavouriteAlbums = true } });
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Album>([new Album { Id = "al1", Name = "Discovery" }], 1, 0));
        _library.GetAlbumTracksAsync("al1", Arg.Any<CancellationToken>()).Returns([Song("d1")]);

        _messenger.Send(new FavouriteChangedMessage("al1", true));
        _time.Advance(DownloadManager.ChangeDelay);

        var until = DateTime.UtcNow.AddSeconds(10);
        while ((_sut.Status.Downloaded < 1 || _sut.Status.IsSyncing) && DateTime.UtcNow < until)
        {
            await Task.Delay(50, Ct);
        }

        Files().Should().Equal("d1.flac");
    }

    [Fact]
    public async Task The_estimate_counts_only_the_songs_not_on_disk_with_their_file_sizes()
    {
        _settings.Update(s => s with { Downloads = new DownloadSettings { FavouriteSongs = true } });
        await _sut.SyncAsync();
        Playlist("b", "c", "x");
        _files["c"] = new TrackFile("flac", 30_000_000);

        var estimate = await _sut.EstimateAsync(new DownloadSettings { Playlists = true }, Ct);

        // b is on disk already as a favourite; x has no size, so it counts nothing without a length.
        estimate.Should().Be(new DownloadEstimate(2, 30_000_000));
        _source.Requests.Should().HaveCount(2, "estimating downloads nothing");
    }

    [Fact]
    public async Task The_estimate_for_aac_is_the_length_at_the_bitrate()
    {
        _settings.Update(s => s with { Downloads = new DownloadSettings() });
        _playlists.GetEntriesAsync(PlaylistId, Arg.Any<CancellationToken>())
            .Returns([new PlaylistEntry("e-a", Song("a") with { Duration = TimeSpan.FromMinutes(4) })]);

        var estimate = await _sut.EstimateAsync(new DownloadSettings { Playlists = true, Format = DownloadFormatKind.Aac, AacBitrateKbps = 256 }, Ct);

        estimate.Should().Be(new DownloadEstimate(1, 240 * 256 * 125));
    }

    [Fact]
    public async Task A_downloaded_song_has_its_file_and_another_one_none()
    {
        await _sut.SyncAsync();

        _sut.FileOf("a").Should().Be(Path.Join(Folder, "a.flac"));
        _sut.FileOf("unknown").Should().BeNull();
        File.Delete(Path.Join(Folder, "a.flac"));
        _sut.FileOf("a").Should().BeNull("the file is gone from disk");
    }

    [Fact]
    public async Task The_files_are_known_from_the_first_song_after_start_up()
    {
        await _sut.SyncAsync();
        using var later = new DownloadManager(
            _client, _library, _playlists, _source, _store, _settings, _network, Substitute.For<IServerManager>(), new WeakReferenceMessenger(), _time, NullLogger<DownloadManager>.Instance);

        later.FileOf("c").Should().Be(Path.Join(Folder, "c.flac"));
    }

    [Fact]
    public async Task Signed_out_no_song_has_a_file()
    {
        await _sut.SyncAsync();
        _client.Connection.ReturnsNull();

        _sut.FileOf("a").Should().BeNull();
    }

    [Fact]
    public async Task The_covers_of_the_downloads_are_kept_and_found_while_offline()
    {
        var images = Substitute.For<IImageSource>();
        images.OpenAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>()).Returns(_ => (Stream)new MemoryStream([1, 2, 3]));
        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([Song("a") with { AlbumId = "al1", AlbumImageTag = "t1" }]);
        using var sut = new DownloadManager(
            _client, _library, _playlists, _source, _store, _settings, _network, Substitute.For<IServerManager>(), new WeakReferenceMessenger(), _time, NullLogger<DownloadManager>.Instance, images);

        await sut.SyncAsync();

        var cover = Path.Join(_store.CoversOf(_client.Connection!.UserDataKey), "al1_t1.img");
        File.ReadAllBytes(cover).Should().Equal(1, 2, 3);
        await images.Received(1).OpenAsync(Arg.Is<ImageRequest>(r => r.ItemId == "al1" && r.Width == DownloadManager.CoverWidth), Arg.Any<CancellationToken>());
        sut.Find(new ImageRequest("al1", ImageKind.Primary, "t1", 200)).Should().Be(cover);
        sut.Find(new ImageRequest("al1", ImageKind.Primary, "t2", 200)).Should().Be(cover, "a changed tag still finds the older cover");
        sut.Find(new ImageRequest("other", ImageKind.Primary, "t1", 200)).Should().BeNull();

        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        await sut.SyncAsync();
        File.Exists(cover).Should().BeFalse("no download needs it any more");
    }

    [Fact]
    public async Task The_downloaded_songs_and_complete_collections_are_known_and_changes_raised()
    {
        var changes = 0;
        _sut.Changed += (_, _) => Interlocked.Increment(ref changes);
        _source.Failures["c"] = new JellyfinException("Forbidden", HttpStatusCode.Forbidden);

        await _sut.SyncAsync();

        _sut.IsDownloaded("a").Should().BeTrue();
        _sut.IsDownloaded("c").Should().BeFalse();
        _sut.IsComplete(PlaylistId).Should().BeFalse("c of the playlist failed");
        changes.Should().BePositive();

        _source.Failures.Remove("c");
        await _sut.SyncNowAsync();
        _sut.IsComplete(PlaylistId).Should().BeTrue();
        _sut.IsComplete("unknown").Should().BeFalse();
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

    [Fact]
    public async Task Moving_the_downloads_saves_the_folder_and_downloads_nothing_again()
    {
        await _sut.SyncAsync();

        var result = await _sut.MoveAsync(_chosen, cancellationToken: Ct);

        result.Outcome.Should().Be(DownloadMoveOutcome.Moved);
        _settings.Current.Downloads.Folder.Should().Be(Path.GetFullPath(_chosen));
        _sut.Folder.Should().Be(Path.Join(Path.GetFullPath(_chosen), DownloadStoreOptions.FolderName));
        Folder.Should().StartWith(_sut.Folder);
        Files().Should().Equal("a.flac", "b.flac", "c.flac");
        _sut.FileOf("a").Should().Be(Path.Join(Folder, "a.flac"));
        Directory.Exists(_directory).Should().BeFalse();

        await _sut.SyncAsync();

        _source.Requests.Should().HaveCount(3, "the moved files are found");
        _sut.Status.Should().BeEquivalentTo(new { Downloaded = 3, Bytes = 12L, Stopped = DownloadStop.None });
    }

    [Fact]
    public async Task Moving_back_to_the_default_folder_forgets_the_chosen_one()
    {
        await _sut.SyncAsync();
        await _sut.MoveAsync(_chosen, cancellationToken: Ct);

        var result = await _sut.MoveAsync(null, cancellationToken: Ct);

        result.Outcome.Should().Be(DownloadMoveOutcome.Moved);
        _settings.Current.Downloads.Folder.Should().BeNull();
        _sut.Folder.Should().Be(_directory);
        Files().Should().Equal("a.flac", "b.flac", "c.flac");
    }

    [Fact]
    public async Task A_move_that_is_refused_keeps_the_folder_setting()
    {
        await _sut.SyncAsync();

        var result = await _sut.MoveAsync(Path.Join(_directory, "inside"), cancellationToken: Ct);

        result.Outcome.Should().Be(DownloadMoveOutcome.Inside);
        _settings.Current.Downloads.Folder.Should().BeNull();
        Files().Should().Equal("a.flac", "b.flac", "c.flac");
    }

    [Fact]
    public async Task Without_the_folders_drive_nothing_is_deleted_or_downloaded_again()
    {
        await _sut.SyncAsync();
        _driveReady = false;

        // Started while the drive is not there: the index it reads then is not kept.
        using var later = new DownloadManager(
            _client, _library, _playlists, _source, _store, _settings, _network, _servers, _messenger, _time, NullLogger<DownloadManager>.Instance);
        await later.RefreshStatusAsync();
        later.FileOf("a").Should().BeNull();
        await later.SyncAsync();

        later.Status.Stopped.Should().Be(DownloadStop.FolderMissing);
        _driveReady = true;
        await later.SyncAsync();

        Files().Should().Equal("a.flac", "b.flac", "c.flac");
        _source.Requests.Should().HaveCount(3);
        later.FileOf("a").Should().Be(Path.Join(Folder, "a.flac"));
    }

    private DownloadItem Item(DownloadItemKind kind) => _sut.GetItems().Single(i => i.Kind == kind);

    [Fact]
    public async Task Each_download_is_listed_with_its_songs_and_size()
    {
        await _sut.SyncAsync();

        var items = _sut.GetItems();

        items.Select(i => (i.Kind, i.Name)).Should().Equal((DownloadItemKind.FavouriteSongs, string.Empty), (DownloadItemKind.Playlist, "Road trip"));
        items.Should().OnlyContain(i => i.State == DownloadItemState.Downloaded && i.Downloaded == 2 && i.Bytes == 8);
        Item(DownloadItemKind.Playlist).TrackIds.Should().Equal("b", "c");
        _sut.FreeBytes().Should().Be(_freeSpace);
    }

    [Fact]
    public async Task Removing_a_playlist_keeps_the_songs_another_download_has()
    {
        await _sut.SyncAsync();

        _sut.Remove(Item(DownloadItemKind.Playlist));
        await _sut.SyncAsync();

        // b is a favourite too; c only was in the playlist.
        Files().Should().Equal("a.flac", "b.flac");
        _sut.GetItems().Should().ContainSingle().Which.Kind.Should().Be(DownloadItemKind.FavouriteSongs);
        _sut.Removed.Should().Equal(new DownloadExclusion(DownloadItemKind.Playlist, PlaylistId, "Road trip"));
        _settings.Current.Downloads.Playlists.Should().BeTrue("the other playlists still download");

        _sut.Restore(_sut.Removed[0]);
        await _sut.SyncAsync();

        Files().Should().Equal("a.flac", "b.flac", "c.flac");
        _sut.Removed.Should().BeEmpty();
        _settings.Current.Downloads.Removed.Should().BeEmpty();
    }

    [Fact]
    public async Task What_was_removed_is_left_out_of_the_size_asked_about()
    {
        _settings.Update(s => s with
        {
            Downloads = s.Downloads with
            {
                Removed = new Dictionary<string, IReadOnlyList<DownloadExclusion>>
                {
                    [_client.Connection!.UserDataKey] = [new DownloadExclusion(DownloadItemKind.Playlist, PlaylistId, "Road trip")],
                },
            },
        });

        var estimate = await _sut.EstimateAsync(_settings.Current.Downloads, Ct);

        estimate.Songs.Should().Be(2, "the playlist's own song is not wanted");
    }

    [Fact]
    public async Task Removing_the_favourite_songs_turns_their_switch_off()
    {
        await _sut.SyncAsync();

        _sut.Remove(Item(DownloadItemKind.FavouriteSongs));
        await _sut.SyncAsync();

        _settings.Current.Downloads.FavouriteSongs.Should().BeFalse();
        _sut.Removed.Should().BeEmpty();
        Files().Should().Equal("b.flac", "c.flac");
    }

    [Fact]
    public async Task A_failed_download_says_why_and_retry_tries_its_songs_again()
    {
        _source.Failures["c"] = new JellyfinException("Forbidden", HttpStatusCode.Forbidden);
        for (var i = 0; i < DownloadManager.MaxAttempts; i++)
        {
            await _sut.SyncAsync();
        }

        var playlist = Item(DownloadItemKind.Playlist);
        playlist.State.Should().Be(DownloadItemState.Failed);
        playlist.Failures.Should().ContainSingle().Which.Reason.Should().Be("HTTP 403");
        playlist.Downloaded.Should().Be(1);
        Item(DownloadItemKind.FavouriteSongs).State.Should().Be(DownloadItemState.Downloaded);

        _source.Failures.Remove("c");
        await _sut.RetryAsync(playlist);

        Item(DownloadItemKind.Playlist).State.Should().Be(DownloadItemState.Downloaded);
        _source.Count("c").Should().Be(DownloadManager.MaxAttempts + 1);
    }
}
