using Groovefin.Core.Images;

namespace Groovefin.Core.Tests.Images;

public sealed class LruCacheTests
{
    [Fact]
    public void Least_recently_used_entry_is_dropped_at_capacity()
    {
        var sut = new LruCache<string, int>(2);
        sut.Set("a", 1);
        sut.Set("b", 2);
        sut.TryGet("a", out _);

        sut.Set("c", 3);

        sut.TryGet("a", out var a).Should().BeTrue();
        a.Should().Be(1);
        sut.TryGet("b", out _).Should().BeFalse();
        sut.TryGet("c", out _).Should().BeTrue();
        sut.Count.Should().Be(2);
    }

    [Fact]
    public void Set_replaces_an_existing_value_without_growing()
    {
        var sut = new LruCache<string, int>(2);
        sut.Set("a", 1);

        sut.Set("a", 5);

        sut.Count.Should().Be(1);
        sut.TryGet("a", out var value).Should().BeTrue();
        value.Should().Be(5);
    }

    [Fact]
    public void Clear_removes_everything()
    {
        var sut = new LruCache<string, int>(2);
        sut.Set("a", 1);

        sut.Clear();

        sut.Count.Should().Be(0);
        sut.TryGet("a", out _).Should().BeFalse();
    }

    [Fact]
    public void Capacity_must_be_positive()
    {
        var act = () => new LruCache<string, int>(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
