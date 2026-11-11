using Groovefin.Core.Downloads;
using Groovefin.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Core.Tests.Downloads;

public sealed class JsonDownloadStoreTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly JsonDownloadStore _sut;

    public JsonDownloadStoreTests() => _sut = new JsonDownloadStore(new DownloadStoreOptions(_directory), NullLogger<JsonDownloadStore>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task An_index_without_the_defaulted_properties_keeps_their_defaults()
    {
        // A property a file does not have keeps its default, not null or 0 (#355): the index stays version 1,
        // and lists the file leaves out are empty.
        var folder = _sut.FolderOf("user-server");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Join(folder, JsonDownloadStore.IndexFileName), """
            {"playlists":[{"playlist":{"id":"p1","name":"Road trip"}}],
             "albums":[{"album":{"id":"al1","name":"Discovery"}}],
             "artists":[{"artist":{"id":"ar1","name":"Daft Punk"}}]}
            """, Ct);

        var loaded = await _sut.LoadAsync("user-server", Ct);

        loaded.Version.Should().Be(DownloadIndex.CurrentVersion);
        loaded.Tracks.Should().NotBeNull().And.BeEmpty();
        loaded.Playlists.Should().ContainSingle().Which.TrackIds.Should().NotBeNull().And.BeEmpty();
        var album = loaded.Albums.Should().ContainSingle().Subject;
        album.TrackIds.Should().NotBeNull().And.BeEmpty();
        album.Album.AlbumArtists.Should().NotBeNull().And.BeEmpty();
        album.Album.Genres.Should().NotBeNull().And.BeEmpty();
        var artist = loaded.Artists.Should().ContainSingle().Subject;
        artist.TrackIds.Should().NotBeNull().And.BeEmpty();
        artist.Artist.Genres.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task A_saved_index_loads_back_per_key()
    {
        var at = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        var track = new Track { Id = "t1", Name = "Digital Love", Artists = [new ArtistRef("x", "Daft Punk")], Duration = TimeSpan.FromMinutes(5) };
        var index = new DownloadIndex
        {
            FavouriteSongs = ["t1"],
            Playlists = [new DownloadedPlaylist { Playlist = new Playlist { Id = "p1", Name = "Road trip" }, TrackIds = ["t1"] }],
            LastSync = at,
        };
        index.Tracks["t1"] = new DownloadedTrack { Track = track, FileName = "t1.flac", Size = 30, Format = "original", DownloadedAt = at };

        await _sut.SaveAsync("user-server", index, Ct);
        var loaded = await _sut.LoadAsync("user-server", Ct);

        loaded.Should().BeEquivalentTo(index);
        File.Exists(Path.Join(_directory, "user-server", JsonDownloadStore.IndexFileName)).Should().BeTrue();
        (await _sut.LoadAsync("other-user", Ct)).Tracks.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unreadable_index_is_an_empty_one()
    {
        Directory.CreateDirectory(_sut.FolderOf("user-server"));
        await File.WriteAllTextAsync(Path.Join(_sut.FolderOf("user-server"), JsonDownloadStore.IndexFileName), "{ not json", Ct);

        (await _sut.LoadAsync("user-server", Ct)).Tracks.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAll_removes_the_folder_with_its_files()
    {
        await _sut.SaveAsync("user-server", new DownloadIndex(), Ct);
        await File.WriteAllTextAsync(Path.Join(_sut.FolderOf("user-server"), "t1.flac"), "x", Ct);

        _sut.DeleteAll("user-server");
        _sut.DeleteAll("never-made");

        Directory.Exists(_sut.FolderOf("user-server")).Should().BeFalse();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("")]
    public void A_key_that_is_no_folder_name_is_rejected(string key) =>
        _sut.Invoking(s => s.FolderOf(key)).Should().Throw<ArgumentException>();

    private string IndexPath => Path.Join(_sut.FolderOf("user-server"), JsonDownloadStore.IndexFileName);

    private static DownloadIndex IndexWith(string trackId)
    {
        var index = new DownloadIndex();
        index.Tracks[trackId] = new DownloadedTrack { Track = new Track { Id = trackId, Name = trackId }, FileName = trackId + ".flac", Format = "original" };
        return index;
    }

    [Fact]
    public async Task A_damaged_index_gives_way_to_its_backup_and_is_kept()
    {
        await _sut.SaveAsync("user-server", IndexWith("t1"), Ct);
        await _sut.SaveAsync("user-server", IndexWith("t2"), Ct);
        await File.WriteAllTextAsync(IndexPath, "{ \"tracks\": { \"t2\": ", Ct);

        var loaded = await _sut.LoadAsync("user-server", Ct);

        loaded.Tracks.Keys.Should().Equal("t1");
        File.Exists(IndexPath + ".corrupt").Should().BeTrue();
    }

    [Fact]
    public async Task An_index_lost_between_the_renames_of_a_save_comes_from_its_backup()
    {
        await _sut.SaveAsync("user-server", IndexWith("t1"), Ct);
        File.Move(IndexPath, IndexPath + ".bak");

        (await _sut.LoadAsync("user-server", Ct)).Tracks.Keys.Should().Equal("t1");
    }

    [Fact]
    public async Task An_index_in_use_is_not_an_empty_one_and_is_not_saved_over()
    {
        await _sut.SaveAsync("user-server", IndexWith("t1"), Ct);
        var before = await File.ReadAllBytesAsync(IndexPath, Ct);

        await using (new FileStream(IndexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await _sut.Awaiting(s => s.LoadAsync("user-server", Ct)).Should().ThrowAsync<IOException>();
        }

        await _sut.SaveAsync("user-server", new DownloadIndex(), Ct);
        (await File.ReadAllBytesAsync(IndexPath, Ct)).Should().Equal(before);

        // Read again, it is saved again.
        (await _sut.LoadAsync("user-server", Ct)).Tracks.Keys.Should().Equal("t1");
        await _sut.SaveAsync("user-server", IndexWith("t2"), Ct);
        (await _sut.LoadAsync("user-server", Ct)).Tracks.Keys.Should().Equal("t2");
    }

    [Fact]
    public async Task An_index_of_a_newer_version_is_not_an_empty_one_and_is_not_saved_over()
    {
        Directory.CreateDirectory(_sut.FolderOf("user-server"));
        const string Newer = """{"version":2,"tracks":{},"somethingNew":true}""";
        await File.WriteAllTextAsync(IndexPath, Newer, Ct);

        await _sut.Awaiting(s => s.LoadAsync("user-server", Ct)).Should().ThrowAsync<IOException>();
        await _sut.SaveAsync("user-server", new DownloadIndex(), Ct);

        (await File.ReadAllTextAsync(IndexPath, Ct)).Should().Be(Newer);
    }

    [Fact]
    public async Task Rename_gives_a_folder_another_key_unless_that_key_has_one()
    {
        await _sut.SaveAsync("old", IndexWith("t1"), Ct);
        await _sut.SaveAsync("taken", IndexWith("t2"), Ct);

        _sut.Rename("old", "new");
        _sut.Rename("new", "taken");
        _sut.Rename("never-made", "other");

        _sut.Keys().Should().BeEquivalentTo(["new", "taken"]);
        (await _sut.LoadAsync("new", Ct)).Tracks.Keys.Should().Equal("t1");
        (await _sut.LoadAsync("taken", Ct)).Tracks.Keys.Should().Equal("t2");
    }
}
