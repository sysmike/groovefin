using System.Globalization;
using Groovefin.Core.Services;

namespace Groovefin.Core.Mixes;

/// <summary>A mix's rules in a line (#401), e.g. "Rock · 1990–1999 · Not played for 6 months · Random · 50 songs".</summary>
public static class MixSummary
{
    public static string Describe(MixRules rules, IStringResources strings)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(strings);
        var parts = new List<string>();
        if (rules.GenreName is { Length: > 0 } genre)
        {
            parts.Add(genre);
        }

        if (rules.FromYear is not null || rules.ToYear is not null)
        {
            parts.Add(rules.FromYear == rules.ToYear
                ? Number(rules.FromYear!.Value)
                : $"{(rules.FromYear is { } from ? Number(from) : string.Empty)}–{(rules.ToYear is { } to ? Number(to) : string.Empty)}");
        }

        if (rules.FavouritesOnly)
        {
            parts.Add(strings.GetString("Mix_Favourites"));
        }

        if (MixEditorPresets.Plays.IndexOf(rules.MinPlays, rules.MaxPlays) is > 0 and var plays)
        {
            parts.Add(strings.GetString(MixEditorPresets.Plays.Keys[plays]));
        }

        if (MixEditorPresets.NotPlayed.IndexOf(rules.NotPlayedForDays) is > 0 and var notPlayed)
        {
            parts.Add(strings.GetString(MixEditorPresets.NotPlayed.Keys[notPlayed]));
        }

        if (MixEditorPresets.Added.IndexOf(rules.AddedWithinDays) is > 0 and var added)
        {
            parts.Add(strings.GetString(MixEditorPresets.Added.Keys[added]));
        }

        parts.Add(strings.GetString(MixEditorPresets.OrderKeys[(int)rules.Order]));
        parts.Add(strings.GetString("Mix_Size").Replace("{0}", Number(rules.Size), StringComparison.Ordinal));
        return string.Join(" · ", parts);
    }

    private static string Number(int value) => value.ToString(CultureInfo.CurrentCulture);
}
