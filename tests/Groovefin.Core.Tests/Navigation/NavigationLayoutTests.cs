using Groovefin.Core.Navigation;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Tests.Navigation;

public sealed class NavigationLayoutTests
{
    private static NavigationItemSettings Item(string key, bool isVisible = true) => new() { Key = key, IsVisible = isVisible };

    [Fact]
    public void Without_a_saved_layout_every_section_shows_in_the_default_order()
    {
        NavigationLayout.Resolve([]).Should().BeEquivalentTo(
            PageKeys.MainSections.Select(k => Item(k)), o => o.WithStrictOrdering());
        NavigationLayout.Resolve(null).Should().HaveCount(PageKeys.MainSections.Count);
    }

    [Fact]
    public void The_saved_order_and_visibility_are_kept_and_missing_sections_are_added_at_the_end()
    {
        var layout = NavigationLayout.Resolve([Item(PageKeys.Songs), Item(PageKeys.Home, isVisible: false), Item(PageKeys.Albums)]);

        layout.Select(i => i.Key).Should().Equal(
            [PageKeys.Songs, PageKeys.Home, PageKeys.Albums, .. PageKeys.MainSections.Except([PageKeys.Songs, PageKeys.Home, PageKeys.Albums])]);
        layout.Where(i => !i.IsVisible).Select(i => i.Key).Should().Equal(PageKeys.Home);
    }

    [Fact]
    public void Unknown_and_repeated_keys_are_dropped()
    {
        var layout = NavigationLayout.Resolve([Item("removed-section"), Item(PageKeys.Genres, isVisible: false), Item(PageKeys.Genres), Item(string.Empty)]);

        layout.Select(i => i.Key).Should().Equal([PageKeys.Genres, .. PageKeys.MainSections.Except([PageKeys.Genres])]);
        layout[0].IsVisible.Should().BeFalse();
    }

    [Fact]
    public void When_nothing_would_show_the_first_section_does()
    {
        var saved = PageKeys.MainSections.Reverse().Select(k => Item(k, isVisible: false)).ToList();

        NavigationLayout.VisibleSections(saved).Should().Equal(PageKeys.OnRepeat);
        saved.Should().OnlyContain(i => !i.IsVisible, "the saved layout is not changed");
    }

    [Fact]
    public void Visible_sections_are_the_shown_keys_in_order()
    {
        NavigationLayout.VisibleSections([Item(PageKeys.Favourites), Item(PageKeys.Home, isVisible: false)])
            .Should().Equal([PageKeys.Favourites, .. PageKeys.MainSections.Except([PageKeys.Favourites, PageKeys.Home])]);
    }
}
