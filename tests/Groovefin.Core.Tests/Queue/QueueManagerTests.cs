using Groovefin.Core.Models;
using Groovefin.Core.Queue;

namespace Groovefin.Core.Tests.Queue;

public sealed class QueueManagerTests
{
    private readonly QueueManager _sut = new(new Random(42));
    private int _changes;

    public QueueManagerTests() => _sut.Changed += (_, _) => _changes++;

    private static Track[] Tracks(params string[] names) => names.Select(n => new Track { Id = n, Name = n }).ToArray();

    private IEnumerable<string> Order => _sut.Items.Select(i => i.Track.Name);

    private string? CurrentName => _sut.Current?.Track.Name;

    private string IdOf(string name) => _sut.Items.First(i => i.Track.Name == name).Id;

    private void Start(params string[] names) => _sut.PlayNow(Tracks(names));

    [Fact]
    public void A_new_queue_is_empty()
    {
        _sut.Items.Should().BeEmpty();
        _sut.Current.Should().BeNull();
        _sut.CurrentIndex.Should().Be(-1);
        _sut.History.Should().BeEmpty();
        _sut.Repeat.Should().Be(RepeatMode.Off);
        _sut.Shuffle.Should().BeFalse();
        _sut.PeekNext().Should().BeNull();
        _sut.Next().Should().BeNull();
        _sut.Previous().Should().BeNull();
        _sut.TrackEnded().Should().BeNull();
    }

    [Fact]
    public void Default_constructor_works_and_null_random_is_rejected()
    {
        var unobserved = new QueueManager();
        unobserved.AddLast(Tracks("a"));
        unobserved.Items.Should().ContainSingle();
        var act = () => new QueueManager(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PlayNow_replaces_the_queue_and_starts_at_the_given_track()
    {
        Start("a", "b");

        _sut.PlayNow(Tracks("x", "y", "z"), startIndex: 1);

        Order.Should().Equal("x", "y", "z");
        CurrentName.Should().Be("y");
        _sut.CurrentIndex.Should().Be(1);
        _sut.History.Select(i => i.Track.Name).Should().Equal("a");
        _changes.Should().Be(2);
    }

    [Fact]
    public void Entries_of_the_same_track_have_distinct_ids()
    {
        var track = Tracks("a")[0];

        _sut.PlayNow([track, track]);

        _sut.Items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void PlayNow_with_no_tracks_clears_the_current_entry()
    {
        Start("a");

        _sut.PlayNow([]);

        _sut.Items.Should().BeEmpty();
        _sut.Current.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void PlayNow_rejects_a_start_index_outside_the_tracks(int startIndex)
    {
        var act = () => _sut.PlayNow(Tracks("a", "b"), startIndex);

        act.Should().Throw<ArgumentOutOfRangeException>();
        _sut.Items.Should().BeEmpty();
    }

    [Fact]
    public void Null_tracks_are_rejected()
    {
        _sut.Invoking(q => q.PlayNow(null!)).Should().Throw<ArgumentNullException>();
        _sut.Invoking(q => q.PlayNext(null!)).Should().Throw<ArgumentNullException>();
        _sut.Invoking(q => q.AddLast(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PlayNext_inserts_after_the_current_entry()
    {
        Start("a", "b", "c");
        _sut.Next();

        _sut.PlayNext(Tracks("x", "y"));

        Order.Should().Equal("a", "b", "x", "y", "c");
        CurrentName.Should().Be("b");
    }

    [Fact]
    public void PlayNext_without_a_current_entry_inserts_at_the_start()
    {
        _sut.AddLast(Tracks("a"));

        _sut.PlayNext(Tracks("x"));

        Order.Should().Equal("x", "a");
        _sut.Current.Should().BeNull();
    }

    [Fact]
    public void AddLast_appends()
    {
        Start("a");

        _sut.AddLast(Tracks("b", "c"));

        Order.Should().Equal("a", "b", "c");
        CurrentName.Should().Be("a");
    }

    [Theory]
    [InlineData(0, new[] { "a", "b", "x", "y", "c", "d" })]
    [InlineData(1, new[] { "a", "b", "c", "x", "y", "d" })]
    [InlineData(2, new[] { "a", "b", "c", "d", "x", "y" })]
    [InlineData(9, new[] { "a", "b", "c", "d", "x", "y" })]
    [InlineData(int.MaxValue, new[] { "a", "b", "c", "d", "x", "y" })]
    public void InsertUpcoming_inserts_at_a_position_after_the_current_entry(int position, string[] expected)
    {
        Start("a", "b", "c", "d");
        _sut.Next();
        _changes = 0;

        _sut.InsertUpcoming(Tracks("x", "y"), position);

        Order.Should().Equal(expected);
        CurrentName.Should().Be("b");
        _changes.Should().Be(1);
    }

    [Fact]
    public void InsertUpcoming_without_a_current_entry_counts_from_the_start()
    {
        _sut.AddLast(Tracks("a", "b"));

        _sut.InsertUpcoming(Tracks("x"), 1);

        Order.Should().Equal("a", "x", "b");
        _sut.Current.Should().BeNull();
    }

    [Fact]
    public void InsertUpcoming_while_shuffled_keeps_the_entries_in_the_original_order()
    {
        Start("a", "b", "c", "d");
        _sut.Shuffle = true;
        var following = _sut.Items[_sut.CurrentIndex + 1].Track.Name;

        _sut.InsertUpcoming(Tracks("x"), 0);
        _sut.Items[_sut.CurrentIndex + 1].Track.Name.Should().Be("x");
        _sut.Shuffle = false;

        var order = Order.ToList();
        order.Should().HaveCount(5);
        order.IndexOf("x").Should().Be(order.IndexOf(following) - 1);
    }

    [Fact]
    public void InsertUpcoming_rejects_a_negative_position_and_ignores_nothing()
    {
        Start("a");
        _changes = 0;

        _sut.Invoking(q => q.InsertUpcoming(Tracks("x"), -1)).Should().Throw<ArgumentOutOfRangeException>();
        _sut.InsertUpcoming([], 0);

        _changes.Should().Be(0);
    }

    [Fact]
    public void Adding_nothing_changes_nothing()
    {
        Start("a");
        _changes = 0;

        _sut.PlayNext([]);
        _sut.AddLast([]);

        _changes.Should().Be(0);
    }

    [Fact]
    public void Removing_an_entry_before_the_current_one_keeps_the_current_entry()
    {
        Start("a", "b", "c");
        _sut.Next();

        _sut.Remove(IdOf("a")).Should().BeTrue();

        Order.Should().Equal("b", "c");
        CurrentName.Should().Be("b");
        _sut.CurrentIndex.Should().Be(0);
    }

    [Fact]
    public void Removing_an_entry_after_the_current_one_keeps_the_current_entry()
    {
        Start("a", "b", "c");

        _sut.Remove(IdOf("c"));

        CurrentName.Should().Be("a");
    }

    [Fact]
    public void Removing_the_current_entry_makes_the_next_one_current()
    {
        Start("a", "b", "c");

        _sut.Remove(IdOf("a"));

        CurrentName.Should().Be("b");
    }

    [Fact]
    public void Removing_the_current_last_entry_makes_the_previous_one_current()
    {
        Start("a", "b");
        _sut.Next();

        _sut.Remove(IdOf("b"));

        CurrentName.Should().Be("a");
    }

    [Fact]
    public void Removing_the_only_entry_leaves_nothing_current()
    {
        Start("a");

        _sut.Remove(IdOf("a"));

        _sut.Current.Should().BeNull();
        _sut.CurrentIndex.Should().Be(-1);
    }

    [Fact]
    public void Removing_an_unknown_entry_returns_false()
    {
        Start("a");
        _changes = 0;

        _sut.Remove("missing").Should().BeFalse();

        _changes.Should().Be(0);
    }

    [Fact]
    public void Move_reorders_and_keeps_the_current_entry()
    {
        Start("a", "b", "c", "d");
        _sut.Next();

        _sut.Move(IdOf("d"), 0).Should().BeTrue();

        Order.Should().Equal("d", "a", "b", "c");
        CurrentName.Should().Be("b");
        _sut.CurrentIndex.Should().Be(2);
    }

    [Fact]
    public void Move_to_the_same_place_changes_nothing()
    {
        Start("a", "b");
        _changes = 0;

        _sut.Move(IdOf("b"), 1).Should().BeTrue();

        _changes.Should().Be(0);
    }

    [Fact]
    public void Move_of_an_unknown_entry_returns_false()
    {
        Start("a");

        _sut.Move("missing", 0).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Move_rejects_an_index_outside_the_queue(int newIndex)
    {
        Start("a", "b");

        var act = () => _sut.Move(IdOf("a"), newIndex);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Move_without_a_current_entry_works()
    {
        _sut.AddLast(Tracks("a", "b"));

        _sut.Move(IdOf("b"), 0);

        Order.Should().Equal("b", "a");
        _sut.CurrentIndex.Should().Be(-1);
    }

    [Fact]
    public void Clear_empties_the_queue_and_keeps_history()
    {
        Start("a", "b");
        _sut.Next();

        _sut.Clear();

        _sut.Items.Should().BeEmpty();
        _sut.Current.Should().BeNull();
        _sut.History.Select(i => i.Track.Name).Should().Equal("a", "b");
    }

    [Fact]
    public void Clearing_an_empty_queue_changes_nothing()
    {
        _sut.Clear();

        _changes.Should().Be(0);
    }

    [Fact]
    public void ClearUpcoming_keeps_the_current_entry_and_those_before_it()
    {
        Start("a", "b", "c", "d");
        _sut.Next();
        _changes = 0;

        _sut.ClearUpcoming();

        Order.Should().Equal("a", "b");
        CurrentName.Should().Be("b");
        _changes.Should().Be(1);
    }

    [Fact]
    public void ClearUpcoming_while_shuffled_also_drops_the_entries_from_the_original_order()
    {
        Start("a", "b", "c", "d");
        _sut.Shuffle = true;
        var current = CurrentName;

        _sut.ClearUpcoming();
        _sut.Shuffle = false;

        Order.Should().Equal(current);
    }

    [Fact]
    public void ClearUpcoming_without_upcoming_entries_changes_nothing()
    {
        _sut.ClearUpcoming();
        Start("a");
        _changes = 0;

        _sut.ClearUpcoming();

        _changes.Should().Be(0);
        Order.Should().Equal("a");
    }

    [Fact]
    public void JumpTo_makes_an_entry_current_and_records_history()
    {
        Start("a", "b", "c");

        _sut.JumpTo(IdOf("c")).Should().BeTrue();

        CurrentName.Should().Be("c");
        _sut.History.Select(i => i.Track.Name).Should().Equal("a");
    }

    [Fact]
    public void JumpTo_the_current_entry_or_an_unknown_one_changes_nothing()
    {
        Start("a", "b");
        _changes = 0;

        _sut.JumpTo(IdOf("a")).Should().BeTrue();
        _sut.JumpTo("missing").Should().BeFalse();

        _changes.Should().Be(0);
        _sut.History.Should().BeEmpty();
    }

    [Fact]
    public void Next_moves_through_the_queue_and_stops_at_the_end_without_repeat()
    {
        Start("a", "b");

        _sut.Next()!.Track.Name.Should().Be("b");
        _sut.Next().Should().BeNull();

        CurrentName.Should().Be("b");
        _sut.History.Select(i => i.Track.Name).Should().Equal("a");
    }

    [Fact]
    public void Repeat_all_wraps_to_the_first_entry()
    {
        Start("a", "b");
        _sut.Repeat = RepeatMode.All;
        _sut.Next();

        _sut.PeekNext()!.Track.Name.Should().Be("a");
        _sut.TrackEnded()!.Track.Name.Should().Be("a");
        CurrentName.Should().Be("a");
    }

    [Fact]
    public void Repeat_one_replays_the_track_when_it_ends_but_next_moves_on()
    {
        Start("a", "b");
        _sut.Repeat = RepeatMode.One;

        _sut.PeekNext()!.Track.Name.Should().Be("a");
        _sut.TrackEnded()!.Track.Name.Should().Be("a");
        CurrentName.Should().Be("a");
        _sut.History.Select(i => i.Track.Name).Should().Equal("a");

        _sut.Next()!.Track.Name.Should().Be("b");
    }

    [Fact]
    public void TrackEnded_without_repeat_advances_and_returns_null_at_the_end()
    {
        Start("a", "b");

        _sut.PeekNext()!.Track.Name.Should().Be("b");
        _sut.TrackEnded()!.Track.Name.Should().Be("b");
        _sut.PeekNext().Should().BeNull();
        _sut.TrackEnded().Should().BeNull();
        CurrentName.Should().Be("b");
    }

    [Fact]
    public void Previous_goes_back_and_stays_on_the_first_entry()
    {
        Start("a", "b");
        _sut.Next();

        _sut.Previous()!.Track.Name.Should().Be("a");
        _changes = 0;
        _sut.Previous()!.Track.Name.Should().Be("a");
        _changes.Should().Be(0);
    }

    [Fact]
    public void Previous_on_the_first_entry_wraps_with_repeat_all()
    {
        Start("a", "b", "c");
        _sut.Repeat = RepeatMode.All;

        _sut.Previous()!.Track.Name.Should().Be("c");
    }

    [Fact]
    public void Previous_on_a_single_entry_with_repeat_all_stays()
    {
        Start("a");
        _sut.Repeat = RepeatMode.All;
        _changes = 0;

        _sut.Previous()!.Track.Name.Should().Be("a");

        _changes.Should().Be(0);
    }

    [Fact]
    public void Setting_repeat_or_shuffle_to_the_same_value_raises_nothing()
    {
        Start("a");
        _changes = 0;

        _sut.Repeat = RepeatMode.Off;
        _sut.Shuffle = false;

        _changes.Should().Be(0);
        _sut.Repeat = RepeatMode.All;
        _changes.Should().Be(1);
    }

    [Fact]
    public void Shuffle_keeps_the_current_entry_first_and_all_entries()
    {
        var names = Enumerable.Range(0, 20).Select(i => $"t{i}").ToArray();
        _sut.PlayNow(Tracks(names), startIndex: 5);

        _sut.Shuffle = true;

        CurrentName.Should().Be("t5");
        _sut.CurrentIndex.Should().Be(0);
        Order.Should().BeEquivalentTo(names);
        Order.Should().NotEqual(names);
    }

    [Fact]
    public void Turning_shuffle_off_restores_the_original_order_and_keeps_the_current_entry()
    {
        var names = Enumerable.Range(0, 10).Select(i => $"t{i}").ToArray();
        Start(names);
        _sut.Shuffle = true;
        _sut.Next();
        _sut.Next();
        var current = CurrentName;

        _sut.Shuffle = false;

        Order.Should().Equal(names);
        CurrentName.Should().Be(current);
        _sut.CurrentIndex.Should().Be(Array.IndexOf(names, current));
    }

    [Fact]
    public void Shuffle_without_a_current_entry_shuffles_everything()
    {
        var names = Enumerable.Range(0, 10).Select(i => $"t{i}").ToArray();
        _sut.AddLast(Tracks(names));

        _sut.Shuffle = true;

        _sut.Current.Should().BeNull();
        Order.Should().BeEquivalentTo(names);

        _sut.Shuffle = false;
        Order.Should().Equal(names);
        _sut.CurrentIndex.Should().Be(-1);
    }

    [Fact]
    public void PlayNow_while_shuffled_starts_with_the_chosen_track()
    {
        _sut.Shuffle = true;
        var names = Enumerable.Range(0, 10).Select(i => $"t{i}").ToArray();

        _sut.PlayNow(Tracks(names), startIndex: 7);

        CurrentName.Should().Be("t7");
        _sut.CurrentIndex.Should().Be(0);
        _sut.Shuffle = false;
        Order.Should().Equal(names);
        CurrentName.Should().Be("t7");
    }

    [Fact]
    public void PlayNext_while_shuffled_plays_next_and_keeps_its_place_in_the_original_order()
    {
        Start("a", "b", "c", "d");
        _sut.Shuffle = true;

        _sut.PlayNext(Tracks("x"));

        _sut.Next()!.Track.Name.Should().Be("x");
        _sut.Shuffle = false;
        Order.Should().Equal("a", "x", "b", "c", "d");
    }

    [Fact]
    public void Move_while_shuffled_changes_only_the_play_order()
    {
        Start("a", "b", "c", "d");
        _sut.Shuffle = true;
        var last = _sut.Items[^1];

        _sut.Move(last.Id, 1);

        _sut.Items[1].Should().Be(last);
        _sut.Shuffle = false;
        Order.Should().Equal("a", "b", "c", "d");
    }

    [Fact]
    public void Remove_while_shuffled_also_removes_from_the_original_order()
    {
        Start("a", "b", "c");
        _sut.Shuffle = true;

        _sut.Remove(IdOf("b"));
        _sut.Shuffle = false;

        Order.Should().Equal("a", "c");
    }

    [Fact]
    public void History_keeps_only_the_most_recent_entries()
    {
        var names = Enumerable.Range(0, QueueManager.HistoryLimit + 10).Select(i => $"t{i}").ToArray();
        Start(names);

        var moves = 0;
        while (_sut.Next() is not null)
        {
            moves++;
        }

        moves.Should().Be(names.Length - 1);
        _sut.History.Should().HaveCount(QueueManager.HistoryLimit);
        _sut.History[^1].Track.Name.Should().Be(names[^2]);
        _sut.History[0].Track.Name.Should().Be(names[9]);
    }
}
