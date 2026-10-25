using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Playback;

public sealed class CurrentTrackTests
{
    private readonly FakePlaybackEngine _engine = new();
    private readonly CurrentTrack _sut;

    public CurrentTrackTests() => _sut = new CurrentTrack(_engine);

    private sealed class Row(string trackId) : ICurrentTrackRow
    {
        public Track Track { get; } = new() { Id = trackId, Name = trackId };

        public bool IsCurrent { get; set; }
    }

    private void Play(string trackId) =>
        _engine.Load(new PlaybackSource($"q-{trackId}", new Uri($"http://stream/{trackId}"), new Track { Id = trackId, Name = trackId }));

    [Fact]
    public void Nothing_is_marked_while_nothing_plays()
    {
        Row[] rows = [new("a"), new("b")];

        _sut.Watch(this, () => rows);

        _sut.TrackId.Should().BeNull();
        rows.Should().OnlyContain(r => !r.IsCurrent);
    }

    [Fact]
    public void Watched_rows_are_marked_at_once_and_follow_the_track()
    {
        Play("b");
        Row[] rows = [new("a"), new("b"), new("c")];

        _sut.Watch(this, () => rows);
        rows.Select(r => r.IsCurrent).Should().Equal(false, true, false);

        Play("c");
        rows.Select(r => r.IsCurrent).Should().Equal(false, false, true);

        _engine.Stop();
        rows.Should().OnlyContain(r => !r.IsCurrent);
    }

    [Fact]
    public void A_track_listed_twice_is_marked_twice()
    {
        Play("a");
        Row[] rows = [new("a"), new("b"), new("a")];

        _sut.Mark(rows);

        rows.Select(r => r.IsCurrent).Should().Equal(true, false, true);
    }

    [Fact]
    public void The_rows_are_read_again_on_each_change()
    {
        var rows = new List<Row> { new("a") };
        _sut.Watch(this, () => rows);

        rows.Add(new Row("b"));
        Play("b");

        rows.Select(r => r.IsCurrent).Should().Equal(false, true);
    }

    [Fact]
    public void A_watched_collection_marks_the_rows_added_to_it()
    {
        Play("b");
        var rows = _sut.Watch(new System.Collections.ObjectModel.ObservableCollection<Row> { new("a") });

        rows.Add(new Row("b"));
        rows.Insert(0, new Row("b"));

        rows.Select(r => r.IsCurrent).Should().Equal(true, false, true);
        Play("a");
        rows.Select(r => r.IsCurrent).Should().Equal(false, true, false);
    }
}
