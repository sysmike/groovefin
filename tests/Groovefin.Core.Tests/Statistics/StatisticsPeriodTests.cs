using System.Globalization;
using Groovefin.Core.Statistics;

namespace Groovefin.Core.Tests.Statistics;

public sealed class StatisticsPeriodTests
{
    private static readonly DateTime Monday = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Unspecified);

    private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

    [Theory]
    [InlineData(DayOfWeek.Monday, 9, 16)]
    [InlineData(DayOfWeek.Sunday, 8, 15)]
    [InlineData(DayOfWeek.Saturday, 7, 14)]
    public void A_week_starts_on_the_regions_first_day_of_the_week(DayOfWeek first, int start, int end)
    {
        var week = StatisticsPeriod.Containing(StatisticsRange.Week, Monday.AddHours(15), first);

        week.Start.Should().Be(Day(2026, 11, start));
        week.End.Should().Be(Day(2026, 11, end));
    }

    [Fact]
    public void Months_and_years_are_calendar_ones()
    {
        var month = StatisticsPeriod.Containing(StatisticsRange.Month, Day(2026, 2, 17), DayOfWeek.Monday);
        var year = StatisticsPeriod.Containing(StatisticsRange.Year, Day(2026, 2, 17), DayOfWeek.Monday);

        month.Should().Be(new StatisticsPeriod(StatisticsRange.Month, Day(2026, 2, 1), Day(2026, 3, 1)));
        year.Should().Be(new StatisticsPeriod(StatisticsRange.Year, Day(2026, 1, 1), Day(2027, 1, 1)));
    }

    [Fact]
    public void The_periods_before_and_after_follow_on_without_gaps()
    {
        var month = StatisticsPeriod.Containing(StatisticsRange.Month, Day(2026, 3, 31), DayOfWeek.Monday);

        month.Previous.Should().Be(new StatisticsPeriod(StatisticsRange.Month, Day(2026, 2, 1), Day(2026, 3, 1)));
        month.Next.Should().Be(new StatisticsPeriod(StatisticsRange.Month, Day(2026, 4, 1), Day(2026, 5, 1)));
        StatisticsPeriod.Containing(StatisticsRange.Week, Monday, DayOfWeek.Monday).Previous.End.Should().Be(Monday);
        StatisticsPeriod.Containing(StatisticsRange.Year, Monday, DayOfWeek.Monday).Next.Start.Should().Be(Day(2027, 1, 1));
    }

    [Fact]
    public void All_time_has_no_period_before_or_after()
    {
        var all = StatisticsPeriod.Containing(StatisticsRange.AllTime, Monday, DayOfWeek.Monday);

        all.Should().Be(StatisticsPeriod.AllTime);
        all.Previous.Should().Be(all);
        all.Next.Should().Be(all);
        all.Contains(Monday).Should().BeTrue();
    }

    [Fact]
    public void A_period_contains_its_days_but_not_the_day_after()
    {
        var week = StatisticsPeriod.Containing(StatisticsRange.Week, Monday, DayOfWeek.Monday);

        week.Contains(Monday).Should().BeTrue();
        week.Contains(Monday.AddDays(7).AddTicks(-1)).Should().BeTrue();
        week.Contains(Monday.AddDays(7)).Should().BeFalse();
        week.Contains(Monday.AddTicks(-1)).Should().BeFalse();
    }

    [Theory]
    [InlineData("en-US", StatisticsRange.Week, "11/9/2026 – 11/15/2026")]
    [InlineData("en-US", StatisticsRange.Month, "November 2026")]
    [InlineData("en-US", StatisticsRange.Year, "2026")]
    [InlineData("de-DE", StatisticsRange.Week, "09.11.2026 – 15.11.2026")]
    [InlineData("de-DE", StatisticsRange.Month, "November 2026")]
    public void A_period_is_named_in_the_culture(string culture, StatisticsRange range, string name) =>
        StatisticsPeriod.Containing(range, Monday, DayOfWeek.Monday).Name(CultureInfo.GetCultureInfo(culture)).Should().Be(name);
}
