using System.Globalization;
using System.Text.RegularExpressions;

namespace Groovefin.Core.Lyrics;

/// <summary>
/// Parses LRC lyrics: <c>[mm:ss.xx]text</c> lines (several time tags per line repeat it), metadata
/// tags such as <c>[ti:]</c>, <c>[ar:]</c>, <c>[al:]</c>, <c>[by:]</c>, <c>[length:]</c> and
/// <c>[offset:±ms]</c>, and enhanced LRC word times (<c>&lt;mm:ss.xx&gt;</c>, dropped). Text without
/// any time tag is returned as unsynced lyrics.
/// </summary>
public static partial class LrcParser
{
    /// <summary>Gets a value indicating whether <paramref name="text"/> holds at least one LRC time tag.</summary>
    public static bool HasTimeTags(string? text) => text is not null && TimeTag().IsMatch(text);

    public static TrackLyrics Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var metadata = new LyricsMetadata();
        var synced = new List<(TimeSpan Start, int Order, string Text)>();
        var plain = new List<string>();
        foreach (var raw in text.TrimStart('﻿').Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (MetadataTag().Match(line.Trim()) is { Success: true } tag)
            {
                metadata = WithTag(metadata, tag.Groups["key"].Value, tag.Groups["value"].Value.Trim());
                continue;
            }

            var times = new List<TimeSpan>();
            var rest = line.TrimStart();
            while (LeadingTimeTag().Match(rest) is { Success: true } time)
            {
                if (ParseTime(time.Groups["time"].Value) is { } start)
                {
                    times.Add(start);
                }

                rest = rest[time.Length..];
            }

            var lyric = WordTimes().Replace(rest, string.Empty).Trim();
            if (times.Count > 0)
            {
                synced.AddRange(times.Select(t => (t, synced.Count, lyric)));
            }
            else
            {
                plain.Add(lyric);
            }
        }

        if (synced.Count == 0)
        {
            return new TrackLyrics(TrimBlankLines(plain).Select(t => new LyricLine(null, t)).ToList(), metadata);
        }

        // [offset:+500] shows the lines half a second sooner.
        var lines = synced
            .OrderBy(l => l.Start)
            .ThenBy(l => l.Order)
            .Select(l => new LyricLine(Max(l.Start - metadata.Offset, TimeSpan.Zero), l.Text))
            .ToList();
        return new TrackLyrics(lines, metadata);
    }

    /// <summary>Reads <c>mm:ss</c>, <c>mm:ss.xx</c> (up to milliseconds), <c>mm:ss:xx</c> and <c>hh:mm:ss.xx</c>.</summary>
    private static TimeSpan? ParseTime(string value)
    {
        var parts = value.Split(':', '.');
        if (parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit)))
        {
            return null;
        }

        var numbers = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        var (hours, minutes, seconds, fraction) = parts.Length switch
        {
            2 => (0, numbers[0], numbers[1], string.Empty),
            3 => (0, numbers[0], numbers[1], parts[2]),
            4 => (numbers[0], numbers[1], numbers[2], parts[3]),
            _ => (0, 0, 60, string.Empty),
        };
        if (seconds >= 60 || fraction.Length > 3)
        {
            return null;
        }

        var milliseconds = fraction.Length == 0 ? 0 : int.Parse(fraction.PadRight(3, '0'), CultureInfo.InvariantCulture);
        return new TimeSpan(0, hours, minutes, seconds, milliseconds);
    }

    private static LyricsMetadata WithTag(LyricsMetadata metadata, string key, string value) =>
        key.ToLowerInvariant() switch
        {
            "ti" => metadata with { Title = Empty(value) },
            "ar" => metadata with { Artist = Empty(value) },
            "al" => metadata with { Album = Empty(value) },
            "by" => metadata with { By = Empty(value) },
            "length" => metadata with { Length = ParseTime(value) },
            "offset" when int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms) =>
                metadata with { Offset = TimeSpan.FromMilliseconds(ms) },
            _ => metadata,
        };

    private static string? Empty(string value) => value.Length == 0 ? null : value;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static IEnumerable<string> TrimBlankLines(List<string> lines)
    {
        var first = lines.FindIndex(l => l.Length > 0);
        var last = lines.FindLastIndex(l => l.Length > 0);
        return first < 0 ? [] : lines.GetRange(first, last - first + 1);
    }

    [GeneratedRegex(@"\[\d+:\d+([.:]\d+)*\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[(?<time>\d+:\d+(?:[.:]\d+)*)\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LeadingTimeTag();

    [GeneratedRegex(@"^\[(?<key>[A-Za-z]+):(?<value>[^\]]*)\]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MetadataTag();

    [GeneratedRegex(@"<\d+:\d+(?:[.:]\d+)*>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WordTimes();
}
