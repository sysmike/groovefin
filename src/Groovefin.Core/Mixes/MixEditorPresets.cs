namespace Groovefin.Core.Mixes;

/// <summary>
/// The choices the mix editor offers (#401), with their string keys. The rules keep plain numbers; the editor and the
/// summary show the matching choice. The first choice of each is "any".
/// </summary>
public static class MixEditorPresets
{
    public static PlaysPresets Plays { get; } = new(
        ["MixPlays_Any", "MixPlays_Never", "MixPlays_AtLeast1", "MixPlays_AtLeast5", "MixPlays_AtLeast10", "MixPlays_AtLeast25"],
        [(null, null), (null, 0), (1, null), (5, null), (10, null), (25, null)]);

    public static DayPresets NotPlayed { get; } = new(
        ["MixNotPlayed_Any", "MixNotPlayed_Month", "MixNotPlayed_ThreeMonths", "MixNotPlayed_SixMonths", "MixNotPlayed_Year"],
        [null, 30, 91, 182, 365]);

    public static DayPresets Added { get; } = new(
        ["MixAdded_Any", "MixAdded_Week", "MixAdded_Month", "MixAdded_ThreeMonths", "MixAdded_Year"],
        [null, 7, 30, 91, 365]);

    /// <summary>Gets the string keys of <see cref="MixOrder"/>'s values, in its order.</summary>
    public static IReadOnlyList<string> OrderKeys { get; } =
        ["MixOrder_Random", "MixOrder_MostPlayed", "MixOrder_RecentlyAdded", "MixOrder_LeastRecentlyPlayed"];

    public static IReadOnlyList<int> Sizes { get; } = [25, 50, 100, 200];

    public sealed record PlaysPresets(IReadOnlyList<string> Keys, IReadOnlyList<(int? Min, int? Max)> Values)
    {
        /// <summary>The choice with these bounds, or -1 when none has them.</summary>
        public int IndexOf(int? min, int? max) => Values.ToList().IndexOf((min, max));
    }

    public sealed record DayPresets(IReadOnlyList<string> Keys, IReadOnlyList<int?> Values)
    {
        /// <summary>The choice with these days, or -1 when none has them.</summary>
        public int IndexOf(int? days) => Values.ToList().IndexOf(days);
    }
}
