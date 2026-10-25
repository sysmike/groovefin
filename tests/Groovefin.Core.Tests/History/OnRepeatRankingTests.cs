using Groovefin.Core.History;

namespace Groovefin.Core.Tests.History;

public sealed class OnRepeatRankingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static PlayRecord Play(string trackId, double daysAgo, PlaySource source = PlaySource.Groovefin) =>
        new(trackId, Now.AddDays(-daysAgo), source);

    [Fact]
    public void Most_plays_come_first_and_ties_go_to_the_latest_play()
    {
        PlayRecord[] plays =
        [
            Play("a", 1), Play("a", 2),
            Play("b", 3), Play("b", 4), Play("b", 5, PlaySource.Server),
            Play("c", 0.5), Play("c", 9),
        ];

        var ranked = OnRepeatRanking.Rank(plays, Now, PlayHistory.Window);

        ranked.Select(e => e.TrackId).Should().Equal("b", "c", "a");
        ranked[0].Plays.Should().Be(3);
        ranked[1].LastPlayed.Should().Be(Now.AddDays(-0.5));
    }

    [Fact]
    public void Plays_outside_the_window_and_single_plays_do_not_count()
    {
        PlayRecord[] plays = [Play("old", 31), Play("old", 40), Play("once", 1), Play("edge", 29.9), Play("edge", 30.1), Play("edge", 1)];

        var ranked = OnRepeatRanking.Rank(plays, Now, PlayHistory.Window);

        ranked.Should().ContainSingle().Which.Should().Be(new OnRepeatEntry("edge", 2, Now.AddDays(-1)));
    }

    [Fact]
    public void Only_the_first_thirty_are_listed()
    {
        var plays = Enumerable.Range(0, 40).SelectMany(i => new[] { Play($"t{i:00}", 1), Play($"t{i:00}", 2) });

        OnRepeatRanking.Rank(plays, Now, PlayHistory.Window).Should().HaveCount(OnRepeatRanking.Size);
    }
}
