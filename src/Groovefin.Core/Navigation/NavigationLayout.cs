using Groovefin.Core.Settings;

namespace Groovefin.Core.Navigation;

/// <summary>The navigation pane's sections in the user's order and visibility (#255).</summary>
public static class NavigationLayout
{
    /// <summary>
    /// The saved layout made whole: every main section once, in the saved order; keys that are no longer
    /// sections are dropped and new sections are added at the end, shown. At least one section shows.
    /// </summary>
    public static IReadOnlyList<NavigationItemSettings> Resolve(IReadOnlyList<NavigationItemSettings>? saved)
    {
        List<NavigationItemSettings> items =
        [
            .. (saved ?? []).Where(i => PageKeys.MainSections.Contains(i.Key)).DistinctBy(i => i.Key)
                .Select(i => new NavigationItemSettings { Key = i.Key, IsVisible = i.IsVisible }),
        ];

        foreach (var key in PageKeys.MainSections.Where(key => items.TrueForAll(i => i.Key != key)))
        {
            items.Add(new NavigationItemSettings { Key = key });
        }

        if (!items.Exists(i => i.IsVisible))
        {
            items[0].IsVisible = true;
        }

        return items;
    }

    /// <summary>The keys of the sections shown, in order.</summary>
    public static IReadOnlyList<string> VisibleSections(IReadOnlyList<NavigationItemSettings>? saved) =>
        [.. Resolve(saved).Where(i => i.IsVisible).Select(i => i.Key)];
}
