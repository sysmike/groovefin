using Groovefin.Core.Mixes;
using Groovefin.Core.Services;

namespace Groovefin.Core.Tests.Mixes;

public sealed class MixSummaryTests
{
    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public MixSummaryTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Mix_Size").Returns("{0} songs");
    }

    [Fact]
    public void Only_the_rules_in_use_are_listed()
    {
        MixSummary.Describe(new MixRules(), _strings).Should().Be("MixOrder_Random · 50 songs");
    }

    [Fact]
    public void Every_rule_has_its_part()
    {
        var rules = new MixRules
        {
            GenreName = "Rock",
            FromYear = 1990,
            ToYear = 1999,
            FavouritesOnly = true,
            MinPlays = 10,
            NotPlayedForDays = 91,
            AddedWithinDays = 365,
            Order = MixOrder.RecentlyAdded,
            Size = 200,
        };

        MixSummary.Describe(rules, _strings).Should().Be(
            "Rock · 1990–1999 · Mix_Favourites · MixPlays_AtLeast10 · MixNotPlayed_ThreeMonths · MixAdded_Year · MixOrder_RecentlyAdded · 200 songs");
    }

    [Theory]
    [InlineData(1995, 1995, "1995")]
    [InlineData(1995, null, "1995–")]
    [InlineData(null, 1995, "–1995")]
    public void Years_read_as_one_year_or_a_range(int? from, int? to, string expected)
    {
        MixSummary.Describe(new MixRules { FromYear = from, ToYear = to }, _strings).Should().StartWith(expected + " · ");
    }
}
