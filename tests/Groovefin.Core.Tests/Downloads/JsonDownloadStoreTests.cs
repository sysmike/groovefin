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
}
