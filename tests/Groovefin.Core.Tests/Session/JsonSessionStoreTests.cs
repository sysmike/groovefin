using Groovefin.Core.Models;
using Groovefin.Core.Queue;
using Groovefin.Core.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Core.Tests.Session;

public sealed class JsonSessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly JsonSessionStore _sut;

    public JsonSessionStoreTests() => _sut = new JsonSessionStore(new SessionStoreOptions(FilePath), NullLogger<JsonSessionStore>.Instance);

    private string FilePath => Path.Join(_directory, "session.json");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static readonly Track DigitalLove = new()
    {
        Id = "t1",
        Name = "Digital Love",
        AlbumId = "al1",
        AlbumName = "Discovery",
        AlbumImageTag = "tag",
        AlbumArtist = "Daft Punk",
        Artists = [new ArtistRef("ar1", "Daft Punk"), new ArtistRef("ar2", "Romanthony")],
        Duration = TimeSpan.FromSeconds(298),
        TrackNumber = 3,
        DiscNumber = 1,
        Year = 2001,
        DateAdded = new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero),
        IsFavorite = true,
        PlayCount = 7,
        NormalizationGain = -6.5,
    };

    private static SavedSession Session => new()
    {
        ServerId = "srv",
        UserId = "user",
        Queue = new QueueSnapshot
        {
            Tracks = [DigitalLove, new Track { Id = "t2", Name = "Aerodynamic" }],
            PlayOrder = [1, 0],
            CurrentIndex = 1,
            Shuffle = true,
            Repeat = RepeatMode.All,
        },
        Position = TimeSpan.FromSeconds(83.5),
    };

    [Fact]
    public async Task A_saved_session_reads_back_unchanged()
    {
        await _sut.SaveAsync(Session, Ct);

        var loaded = await _sut.LoadAsync(Ct);

        loaded.Should().BeEquivalentTo(Session);
        loaded!.Queue.Tracks[0].Artists.Should().Equal(DigitalLove.Artists);
        File.Exists(FilePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task A_file_without_the_defaulted_properties_keeps_their_defaults()
    {
        // A property a file does not have keeps its default, not null or 0 (#355): an empty file is version 1,
        // and a song without artists has none.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, """{"queue":{"tracks":[{"id":"t1","name":"Digital Love"}]}}""", Ct);

        var loaded = await _sut.LoadAsync(Ct);

        loaded.Should().NotBeNull();
        loaded!.Version.Should().Be(SavedSession.CurrentVersion);
        loaded.ServerId.Should().BeEmpty();
        loaded.UserId.Should().BeEmpty();
        loaded.Queue.PlayOrder.Should().NotBeNull().And.BeEmpty();
        loaded.Queue.CurrentIndex.Should().Be(-1);
        loaded.Queue.Tracks.Should().ContainSingle().Which.Artists.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task A_file_without_the_queue_has_an_empty_one()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{}", Ct);

        var loaded = await _sut.LoadAsync(Ct);

        loaded!.Queue.Should().NotBeNull();
        loaded.Queue.Tracks.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Without_a_file_there_is_no_session() =>
        (await _sut.LoadAsync(Ct)).Should().BeNull();

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "version": 99, "serverId": "srv" }""")]
    [InlineData("null")]
    public async Task A_damaged_or_newer_file_is_no_session(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, content, Ct);

        (await _sut.LoadAsync(Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Clear_removes_the_session()
    {
        await _sut.SaveAsync(Session, Ct);

        await _sut.ClearAsync(Ct);
        await _sut.ClearAsync(Ct);

        File.Exists(FilePath).Should().BeFalse();
        (await _sut.LoadAsync(Ct)).Should().BeNull();
    }
}
