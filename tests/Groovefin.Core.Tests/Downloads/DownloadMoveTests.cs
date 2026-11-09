using Groovefin.Core.Downloads;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Core.Tests.Downloads;

/// <summary>Moving the downloads to a folder of the user's choice (#399).</summary>
public sealed class DownloadMoveTests : IDisposable
{
    private const string Key = "user1-server";

    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly string _chosen = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private bool _sameDrive = true;
    private bool _driveReady = true;
    private bool _local = true;
    private long _freeSpace = long.MaxValue;
    private readonly JsonDownloadStore _sut;

    public DownloadMoveTests()
    {
        _sut = new JsonDownloadStore(
            new DownloadStoreOptions(_directory)
            {
                FreeSpace = _ => _freeSpace,
                SameDrive = (_, _) => _sameDrive,
                DriveReady = path => _driveReady || !path.StartsWith(_directory, StringComparison.Ordinal),
                IsLocal = _ => _local,
            },
            NullLogger<JsonDownloadStore>.Instance);
        Write(Path.Join(_directory, Key, "a.flac"), "aaaa");
        Write(Path.Join(_directory, Key, JsonDownloadStore.IndexFileName), "{}");
        Write(Path.Join(_directory, Key, "covers", "al1_x.img"), "cover");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Target => _sut.RootOf(_chosen);

    public void Dispose()
    {
        foreach (var folder in new[] { _directory, _chosen })
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string[] FilesIn(string folder) => Directory.Exists(folder)
        ? [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).Order(StringComparer.Ordinal)]
        : [];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_file_moves_to_a_Groovefin_Downloads_folder_in_the_chosen_one(bool sameDrive)
    {
        _sameDrive = sameDrive;
        var reports = new List<DownloadMoveProgress>();

        var result = await _sut.MoveAsync(Target, new SynchronousProgress(reports.Add), Ct);

        result.Outcome.Should().Be(DownloadMoveOutcome.Moved);
        Target.Should().Be(Path.Join(Path.GetFullPath(_chosen), DownloadStoreOptions.FolderName));
        _sut.Root.Should().Be(Target);
        _sut.FolderOf(Key).Should().StartWith(Target);
        FilesIn(Target).Should().Equal($"{Key}/a.flac", $"{Key}/covers/al1_x.img", $"{Key}/{JsonDownloadStore.IndexFileName}");
        File.ReadAllText(Path.Join(Target, Key, "a.flac")).Should().Be("aaaa");
        Directory.Exists(_directory).Should().BeFalse("the old folders are deleted once empty");
        reports[^1].Should().Be(new DownloadMoveProgress(11, 11));
    }

    [Fact]
    public void The_chosen_folder_is_used_from_the_start()
    {
        var store = new JsonDownloadStore(new DownloadStoreOptions(_directory) { ChosenFolder = () => _chosen }, NullLogger<JsonDownloadStore>.Instance);

        store.Root.Should().Be(Target);
        store.RootOf(null).Should().Be(_directory);
    }

    [Fact]
    public async Task Without_enough_space_on_the_other_drive_nothing_moves()
    {
        _sameDrive = false;
        _freeSpace = 5;

        var result = await _sut.MoveAsync(Target, null, Ct);

        result.Should().Be(new DownloadMoveResult(DownloadMoveOutcome.NotEnoughSpace, 11, 5));
        _sut.Root.Should().Be(_directory);
        FilesIn(_directory).Should().HaveCount(3);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task A_network_folder_or_one_inside_the_downloads_is_refused()
    {
        (await _sut.MoveAsync(Path.Join(_directory, Key, DownloadStoreOptions.FolderName), null, Ct)).Outcome.Should().Be(DownloadMoveOutcome.Inside);
        (await _sut.MoveAsync(_directory, null, Ct)).Outcome.Should().Be(DownloadMoveOutcome.Same);
        _local = false;
        (await _sut.MoveAsync(Target, null, Ct)).Outcome.Should().Be(DownloadMoveOutcome.NotLocal);

        _sut.Root.Should().Be(_directory);
        FilesIn(_directory).Should().HaveCount(3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_cancelled_move_puts_everything_back(bool sameDrive)
    {
        _sameDrive = sameDrive;
        using var cancel = new CancellationTokenSource();
        var reports = 0;

        // Cancelled after the first file.
        var result = await _sut.MoveAsync(Target, new SynchronousProgress(p =>
        {
            if (p.Moved > 0 && ++reports == 1)
            {
                cancel.Cancel();
            }
        }), cancel.Token);

        result.Outcome.Should().Be(DownloadMoveOutcome.Cancelled);
        _sut.Root.Should().Be(_directory);
        FilesIn(_directory).Should().Equal($"{Key}/a.flac", $"{Key}/covers/al1_x.img", $"{Key}/{JsonDownloadStore.IndexFileName}");
        FilesIn(Target).Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_that_cannot_be_written_there_fails_the_move_and_keeps_the_old_folder()
    {
        _sameDrive = false;

        // A folder where a file should go: it cannot be created.
        Directory.CreateDirectory(Path.Join(Target, Key, "a.flac"));

        var result = await _sut.MoveAsync(Target, null, Ct);

        result.Outcome.Should().Be(DownloadMoveOutcome.Failed);
        _sut.Root.Should().Be(_directory);
        FilesIn(_directory).Should().HaveCount(3);
        FilesIn(Target).Should().BeEmpty();
    }

    [Fact]
    public async Task Without_the_old_drive_the_new_folder_is_used_at_once()
    {
        _driveReady = false;
        _sut.IsAvailable.Should().BeFalse();

        var result = await _sut.MoveAsync(Target, null, Ct);

        result.Outcome.Should().Be(DownloadMoveOutcome.Moved);
        _sut.Root.Should().Be(Target);
        _sut.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task An_old_file_in_use_is_deleted_later()
    {
        _sameDrive = false;
        var old = Path.Join(_directory, Key, "a.flac");
        DownloadMoveResult result;

        // Open as a player opens it: others may read, not delete. Windows refuses the delete; elsewhere it succeeds.
        await using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = await _sut.MoveAsync(Target, null, Ct);
        }

        _sut.RemoveLeftovers();

        result.Outcome.Should().Be(DownloadMoveOutcome.Moved);
        File.Exists(old).Should().BeFalse();
        Directory.Exists(_directory).Should().BeFalse();
        File.ReadAllText(Path.Join(Target, Key, "a.flac")).Should().Be("aaaa");
    }

    /// <summary>Reports at once, on the calling thread: <see cref="Progress{T}"/> posts to the thread pool.</summary>
    private sealed class SynchronousProgress(Action<DownloadMoveProgress> report) : IProgress<DownloadMoveProgress>
    {
        public void Report(DownloadMoveProgress value) => report(value);
    }
}
