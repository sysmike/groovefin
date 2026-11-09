using System.Globalization;

namespace Groovefin.Core.Statistics;

/// <summary>The kinds of period the statistics page shows (#382).</summary>
public enum StatisticsRange
{
    Week,
    Month,
    Year,
    AllTime,
}

/// <summary>
/// A calendar period of the statistics (#382): a week from the region's first day of the week, a calendar month or
/// a calendar year, as local dates; or all time, which has neither start nor end.
/// </summary>
/// <param name="Range">The kind of period.</param>
/// <param name="Start">The first day, local; <see cref="DateTime.MinValue"/> for all time.</param>
/// <param name="End">The day after the last, local; <see cref="DateTime.MaxValue"/> for all time.</param>
public sealed record StatisticsPeriod(StatisticsRange Range, DateTime Start, DateTime End)
{
    public static StatisticsPeriod AllTime { get; } = new(StatisticsRange.AllTime, DateTime.MinValue, DateTime.MaxValue);

    /// <summary>Gets the period of <paramref name="range"/> that holds <paramref name="day"/> (a local date).</summary>
    public static StatisticsPeriod Containing(StatisticsRange range, DateTime day, DayOfWeek firstDayOfWeek)
    {
        var date = day.Date;
        switch (range)
        {
            case StatisticsRange.Week:
                var start = date.AddDays(-(((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7));
                return new StatisticsPeriod(range, start, start.AddDays(7));
            case StatisticsRange.Month:
                var month = new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
                return new StatisticsPeriod(range, month, month.AddMonths(1));
            case StatisticsRange.Year:
                var year = new DateTime(date.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
                return new StatisticsPeriod(range, year, year.AddYears(1));
            default:
                return AllTime;
        }
    }

    public bool IsAllTime => Range == StatisticsRange.AllTime;

    /// <summary>Gets the period before this one; all time has none, so it is itself.</summary>
    public StatisticsPeriod Previous => Range switch
    {
        StatisticsRange.Week => this with { Start = Start.AddDays(-7), End = Start },
        StatisticsRange.Month => this with { Start = Start.AddMonths(-1), End = Start },
        StatisticsRange.Year => this with { Start = Start.AddYears(-1), End = Start },
        _ => this,
    };

    /// <summary>Gets the period after this one; all time has none, so it is itself.</summary>
    public StatisticsPeriod Next => Range switch
    {
        StatisticsRange.Week => this with { Start = End, End = End.AddDays(7) },
        StatisticsRange.Month => this with { Start = End, End = End.AddMonths(1) },
        StatisticsRange.Year => this with { Start = End, End = End.AddYears(1) },
        _ => this,
    };

    /// <summary>Gets a value indicating whether <paramref name="day"/> (a local date) is in the period.</summary>
    public bool Contains(DateTime day) => day >= Start && day < End;

    /// <summary>
    /// Gets the period's name: "11/3/2026 – 11/9/2026", "November 2026" or "2026" (in English). All time has a name
    /// of its own, in the strings.
    /// </summary>
    public string Name(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        switch (Range)
        {
            case StatisticsRange.Week:
                return $"{Start.ToString("d", culture)} – {End.AddDays(-1).ToString("d", culture)}";
            case StatisticsRange.Month:
                return Start.ToString(culture.DateTimeFormat.YearMonthPattern, culture);
            case StatisticsRange.Year:
                return Start.Year.ToString(culture);
            default:
                return string.Empty;
        }
    }
}
