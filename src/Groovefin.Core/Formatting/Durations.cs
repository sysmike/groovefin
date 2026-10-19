using System.Globalization;
using Groovefin.Core.Services;

namespace Groovefin.Core.Formatting;

/// <summary>Formats track and album lengths.</summary>
public static class Durations
{
    /// <summary>Track length: <c>4:05</c>, or <c>1:02:03</c> from one hour.</summary>
    public static string Track(TimeSpan? duration)
    {
        if (duration is not { } d)
        {
            return string.Empty;
        }

        return d.TotalHours >= 1
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}")
            : string.Create(CultureInfo.CurrentCulture, $"{(int)d.TotalMinutes}:{d.Seconds:00}");
    }

    /// <summary>Total length in words, e.g. "1 h 4 min" or "42 min" (localized via resources).</summary>
    public static string Total(TimeSpan duration, IStringResources strings)
    {
        var minutes = (int)Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero);
        var hours = minutes / 60;
        var rest = minutes % 60;
        var template = strings.GetString(hours > 0 ? "Duration_HoursMinutes" : "Duration_Minutes");
        return template
            .Replace("{0}", (hours > 0 ? hours : rest).ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
            .Replace("{1}", rest.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
    }
}
