using Groovefin.Core.Api;
using Groovefin.Core.Images;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Images;

public sealed class DiskImageCacheTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly IImageSource _source = Substitute.For<IImageSource>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private long _maxBytes = 1_000_000;

    public DiskImageCacheTests()
    {
        _source.ServerKey.Returns("server-1");
        _source.OpenAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Stream>(new MemoryStream(Bytes(100))));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private DiskImageCache CreateSut() =>
        new(new ImageCacheOptions(_directory, () => _maxBytes), _source, _time, NullLogger<DiskImageCache>.Instance);

    private static byte[] Bytes(int count) => Enumerable.Repeat((byte)7, count).ToArray();

    private static ImageRequest Request(string id, int width = 256) => new(id, ImageKind.Primary, "tag", width);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Miss_downloads_and_returns_a_local_file()
    {
        var sut = CreateSut();

        var path = await sut.GetAsync(Request("a"), Ct);

        path.Should().NotBeNull().And.StartWith(_directory);
        (await File.ReadAllBytesAsync(path!, Ct)).Should().HaveCount(100);
        await _source.Received(1).OpenAsync(Request("a"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Hit_does_not_download_again()
    {
        var sut = CreateSut();
        var first = await sut.GetAsync(Request("a"), Ct);

        var second = await sut.GetAsync(Request("a"), Ct);

        second.Should().Be(first);
        await _source.Received(1).OpenAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Widths_in_the_same_bucket_share_one_file()
    {
        var sut = CreateSut();

        var a = await sut.GetAsync(Request("a", width: 200), Ct);
        var b = await sut.GetAsync(Request("a", width: 256), Ct);

        b.Should().Be(a);
    }

    [Fact]
    public async Task Different_tags_and_servers_use_different_files()
    {
        var sut = CreateSut();
        var original = await sut.GetAsync(Request("a"), Ct);

        var newTag = await sut.GetAsync(Request("a") with { Tag = "tag-2" }, Ct);
        _source.ServerKey.Returns("server-2");
        var otherServer = await sut.GetAsync(Request("a"), Ct);

        new[] { original, newTag, otherServer }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Concurrent_requests_share_one_download()
    {
        var gate = new TaskCompletionSource<Stream>();
        _source.OpenAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var sut = CreateSut();

        var tasks = Enumerable.Range(0, 5).Select(_ => sut.GetAsync(Request("a"), Ct)).ToList();
        gate.SetResult(new MemoryStream(Bytes(10)));
        var paths = await Task.WhenAll(tasks);

        paths.Distinct().Should().ContainSingle().Which.Should().NotBeNull();
        await _source.Received(1).OpenAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_download_returns_null_and_is_retried_next_time()
    {
        _source.OpenAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<Stream>(new JellyfinException("offline", statusCode: null)),
                _ => Task.FromResult<Stream>(new MemoryStream(Bytes(10))));
        var sut = CreateSut();

        (await sut.GetAsync(Request("a"), Ct)).Should().BeNull();
        (await sut.GetAsync(Request("a"), Ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task Not_connected_returns_null_without_downloading()
    {
        _source.ServerKey.Returns((string?)null);
        var sut = CreateSut();

        (await sut.GetAsync(Request("a"), Ct)).Should().BeNull();
        await _source.DidNotReceiveWithAnyArgs().OpenAsync(default!, Ct);
    }

    [Fact]
    public async Task Least_recently_used_files_are_evicted_above_the_limit()
    {
        _maxBytes = 250; // room for two 100-byte images
        var sut = CreateSut();

        var a = await sut.GetAsync(Request("a"), Ct);
        _time.Advance(TimeSpan.FromMinutes(1));
        var b = await sut.GetAsync(Request("b"), Ct);
        _time.Advance(TimeSpan.FromMinutes(1));
        await sut.GetAsync(Request("a"), Ct); // a is used again, so b is now the oldest
        _time.Advance(TimeSpan.FromMinutes(1));
        var c = await sut.GetAsync(Request("c"), Ct);

        File.Exists(a).Should().BeTrue();
        File.Exists(b).Should().BeFalse();
        File.Exists(c).Should().BeTrue();
        (await sut.GetSizeAsync(Ct)).Should().Be(200);
    }

    [Fact]
    public async Task Hits_mark_a_file_as_used_at_most_once_a_minute()
    {
        var sut = CreateSut();
        var path = await sut.GetAsync(Request("a"), Ct);
        var downloaded = File.GetLastWriteTimeUtc(path!);

        _time.Advance(TimeSpan.FromSeconds(30));
        await sut.GetAsync(Request("a"), Ct);
        File.GetLastWriteTimeUtc(path!).Should().Be(downloaded);

        _time.Advance(DiskImageCache.TouchInterval);
        await sut.GetAsync(Request("a"), Ct);
        File.GetLastWriteTimeUtc(path!).Should().Be(_time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Lowered_limit_applies_on_the_next_download()
    {
        var sut = CreateSut();
        for (var i = 0; i < 5; i++)
        {
            await sut.GetAsync(Request($"item-{i}"), Ct);
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        _maxBytes = 100;
        await sut.GetAsync(Request("new"), Ct);

        (await sut.GetSizeAsync(Ct)).Should().Be(100);
    }

    [Fact]
    public async Task Clear_deletes_everything()
    {
        var sut = CreateSut();
        await sut.GetAsync(Request("a"), Ct);
        await sut.GetAsync(Request("b"), Ct);

        await sut.ClearAsync(Ct);

        (await sut.GetSizeAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Size_of_a_missing_cache_folder_is_zero()
    {
        (await CreateSut().GetSizeAsync(Ct)).Should().Be(0);
    }
}
