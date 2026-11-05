using Groovefin.Core.History;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Core.Tests.History;

public sealed class JsonPlayHistoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly JsonPlayHistoryStore _sut;

    public JsonPlayHistoryStoreTests() => _sut = new JsonPlayHistoryStore(new PlayHistoryStoreOptions(_directory), NullLogger<JsonPlayHistoryStore>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_saved_history_loads_back_per_key()
    {
        var at = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var data = new PlayHistoryData { LastSync = at };
        data.Plays.Add(new PlayRecord("t1", at, PlaySource.Server));
        data.Counts["t1"] = new PlayCountRecord(4, at);
        data.ReportedStarts.Add(new ReportedStart("t2", at));

        await _sut.SaveAsync("user-server", data, Ct);
        var loaded = await _sut.LoadAsync("user-server", Ct);

        loaded!.LastSync.Should().Be(at);
        loaded.Plays.Should().Equal(data.Plays);
        loaded.Counts.Should().Equal(data.Counts);
        loaded.ReportedStarts.Should().Equal(data.ReportedStarts);
        (await _sut.LoadAsync("other-user", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task A_history_from_before_the_offline_plays_loads_with_none_pending()
    {
        // Saved before #339 added "pending": the list must be empty, not null (#353).
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Join(_directory, "user-server.json"), """
            {"version":1,"lastSync":"2026-10-25T12:00:00+00:00","plays":[{"trackId":"t1","playedAt":"2026-10-25T12:00:00+00:00","source":"Server"}],"counts":{"t1":{"playCount":4,"lastPlayed":"2026-10-25T12:00:00+00:00"}},"reportedStarts":[]}
            """, Ct);

        var loaded = await _sut.LoadAsync("user-server", Ct);

        loaded!.Plays.Should().ContainSingle().Which.TrackId.Should().Be("t1");
        loaded.Counts.Should().ContainKey("t1");
        loaded.Pending.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task A_history_with_only_its_version_loads_with_empty_lists()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Join(_directory, "user-server.json"), """{"version":1}""", Ct);

        var loaded = await _sut.LoadAsync("user-server", Ct);

        loaded.Should().NotBeNull();
        loaded!.Plays.Should().NotBeNull().And.BeEmpty();
        loaded.Counts.Should().NotBeNull().And.BeEmpty();
        loaded.ReportedStarts.Should().NotBeNull().And.BeEmpty();
        loaded.Pending.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task An_unreadable_file_is_no_history()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Join(_directory, "user-server.json"), "{ not json", Ct);

        (await _sut.LoadAsync("user-server", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task A_key_that_is_no_file_name_is_refused()
    {
        var act = () => _sut.LoadAsync("../elsewhere", Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
