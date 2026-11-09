using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>The home page's rows as saved in Settings (#394).</summary>
public static class HomeLayout
{
    /// <summary>The rows shown when nothing is saved: the two the home page always had.</summary>
    public static IReadOnlySet<HomeShelfKind> ShownByDefault { get; } = new HashSet<HomeShelfKind> { HomeShelfKind.RecentlyPlayed, HomeShelfKind.RecentlyAdded };

    /// <summary>
    /// The saved layout made whole: every row once, in the saved order; keys that are no longer rows are dropped,
    /// and rows added since follow, hidden unless shown by default. Every row may be hidden.
    /// </summary>
    public static IReadOnlyList<HomeRowSettings> Resolve(IReadOnlyList<HomeRowSettings>? saved)
    {
        List<HomeRowSettings> rows =
        [
            .. (saved ?? []).Where(r => Enum.TryParse<HomeShelfKind>(r.Key, out var kind) && Enum.IsDefined(kind) && r.Key == kind.ToString())
                .DistinctBy(r => r.Key)
                .Select(r => new HomeRowSettings { Key = r.Key, IsVisible = r.IsVisible }),
        ];

        foreach (var kind in Enum.GetValues<HomeShelfKind>().Where(kind => rows.TrueForAll(r => r.Key != kind.ToString())))
        {
            rows.Add(new HomeRowSettings { Key = kind.ToString(), IsVisible = ShownByDefault.Contains(kind) });
        }

        return rows;
    }

    /// <summary>The rows shown, in order.</summary>
    public static IReadOnlyList<HomeShelfKind> VisibleRows(IReadOnlyList<HomeRowSettings>? saved) =>
        [.. Resolve(saved).Where(r => r.IsVisible).Select(r => Enum.Parse<HomeShelfKind>(r.Key))];
}
