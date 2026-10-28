using Groovefin.Core.Navigation;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Navigation;

public sealed class ViewStateStoreTests
{
    private readonly ViewStateStore _store = new();

    private static ViewState At(double offset, int? tab = null, int? focused = null) =>
        new([new ElementState { VerticalOffset = offset, SelectedIndex = tab, FocusedIndex = focused }]);

    [Fact]
    public void Each_entry_gets_its_own_id()
    {
        var first = _store.NewEntryId();
        var second = _store.NewEntryId();

        second.Should().NotBe(first);
    }

    [Fact]
    public void A_saved_state_comes_back_for_its_entry_only()
    {
        var artists = _store.NewEntryId();
        var artist = _store.NewEntryId();
        _store.Save(artists, At(1200, tab: 1, focused: 42));

        _store.Get(artists).Should().BeEquivalentTo(At(1200, tab: 1, focused: 42));
        _store.Get(artist).Should().BeNull("a page opened anew starts at the top");
    }

    [Fact]
    public void Leaving_again_replaces_the_state()
    {
        var entry = _store.NewEntryId();
        _store.Save(entry, At(100));
        _store.Save(entry, At(300));

        _store.Get(entry).Should().BeEquivalentTo(At(300));
        _store.Count.Should().Be(1);
    }

    [Fact]
    public void Entries_that_left_the_stacks_are_forgotten()
    {
        var home = _store.NewEntryId();
        var album = _store.NewEntryId();
        var artist = _store.NewEntryId();
        _store.Save(home, At(10));
        _store.Save(album, At(20));
        _store.Save(artist, At(30));

        // Back from the artist to the album, then a new page: the artist left the forward stack.
        _store.Retain([home, album]);

        _store.Get(home).Should().BeEquivalentTo(At(10));
        _store.Get(album).Should().BeEquivalentTo(At(20));
        _store.Get(artist).Should().BeNull();
    }

    [Fact]
    public void At_most_the_capacity_is_kept_and_the_oldest_saved_go_first()
    {
        var ids = Enumerable.Range(0, ViewStateStore.Capacity + 2).Select(_ => _store.NewEntryId()).ToList();
        foreach (var id in ids)
        {
            _store.Save(id, At(id));
        }

        _store.Count.Should().Be(ViewStateStore.Capacity);
        _store.Get(ids[0]).Should().BeNull();
        _store.Get(ids[1]).Should().BeNull();
        _store.Get(ids[^1]).Should().BeEquivalentTo(At(ids[^1]));
    }

    [Fact]
    public void Saving_again_makes_an_entry_the_newest()
    {
        var ids = Enumerable.Range(0, ViewStateStore.Capacity).Select(_ => _store.NewEntryId()).ToList();
        foreach (var id in ids)
        {
            _store.Save(id, At(id));
        }

        _store.Save(ids[0], At(1));
        _store.Save(_store.NewEntryId(), At(2));

        _store.Get(ids[0]).Should().BeEquivalentTo(At(1));
        _store.Get(ids[1]).Should().BeNull();
    }
}

public sealed class PendingScrollTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void It_scrolls_once_the_content_is_long_enough()
    {
        var scroll = new PendingScroll(1200, _time);

        scroll.Next(1500).Should().Be(1200);
        scroll.IsDone.Should().BeTrue();
        scroll.Reached.Should().BeTrue();
        scroll.Next(2000).Should().BeNull("it scrolls only once");
    }

    [Fact]
    public void While_the_content_loads_it_goes_as_far_as_it_can()
    {
        var scroll = new PendingScroll(1200, _time);

        scroll.Next(0).Should().BeNull();
        scroll.Next(400).Should().Be(400);
        scroll.Next(400).Should().BeNull("nothing new to reach");
        scroll.Next(900).Should().Be(900);
        scroll.IsDone.Should().BeFalse();
        scroll.Next(1199.8).Should().Be(1200, "layout rounds to fractions of a pixel");
        scroll.Reached.Should().BeTrue();
    }

    [Fact]
    public void The_top_needs_no_scrolling()
    {
        var scroll = new PendingScroll(0, _time);

        scroll.IsDone.Should().BeTrue();
        scroll.Reached.Should().BeTrue();
        scroll.Next(1000).Should().BeNull();
    }

    [Fact]
    public void It_gives_up_after_the_timeout()
    {
        var scroll = new PendingScroll(1200, _time);
        scroll.Next(300).Should().Be(300);

        _time.Advance(PendingScroll.Timeout + TimeSpan.FromMilliseconds(1));

        scroll.Next(600).Should().BeNull();
        scroll.IsDone.Should().BeTrue();
        scroll.Reached.Should().BeFalse();
    }

    [Fact]
    public void The_user_scrolling_cancels_it()
    {
        var scroll = new PendingScroll(1200, _time);

        scroll.Cancel();

        scroll.Next(1500).Should().BeNull();
        scroll.Reached.Should().BeFalse();
    }
}
