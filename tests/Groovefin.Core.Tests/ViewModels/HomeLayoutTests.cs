using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class HomeLayoutTests
{
    [Fact]
    public void Nothing_saved_shows_the_two_rows_the_page_always_had()
    {
        var rows = HomeLayout.Resolve([]);

        rows.Select(r => r.Key).Should().Equal(Enum.GetNames<HomeShelfKind>());
        HomeLayout.VisibleRows([]).Should().Equal(HomeShelfKind.RecentlyPlayed, HomeShelfKind.RecentlyAdded);
    }

    [Fact]
    public void The_saved_order_stays_unknown_keys_go_and_new_rows_follow()
    {
        IReadOnlyList<HomeRowSettings> saved =
        [
            new() { Key = nameof(HomeShelfKind.ThisWeekLastYear) },
            new() { Key = "Gone" },
            new() { Key = "3" },
            new() { Key = nameof(HomeShelfKind.RecentlyPlayed), IsVisible = false },
            new() { Key = nameof(HomeShelfKind.ThisWeekLastYear), IsVisible = false },
        ];

        var rows = HomeLayout.Resolve(saved);

        rows.Select(r => r.Key).Should().StartWith([nameof(HomeShelfKind.ThisWeekLastYear), nameof(HomeShelfKind.RecentlyPlayed)])
            .And.HaveCount(Enum.GetValues<HomeShelfKind>().Length)
            .And.OnlyHaveUniqueItems();
        HomeLayout.VisibleRows(saved).Should().Equal(HomeShelfKind.ThisWeekLastYear, HomeShelfKind.RecentlyAdded);
    }

    [Fact]
    public void Every_row_may_be_hidden()
    {
        IReadOnlyList<HomeRowSettings> saved = [.. Enum.GetNames<HomeShelfKind>().Select(k => new HomeRowSettings { Key = k, IsVisible = false })];

        HomeLayout.VisibleRows(saved).Should().BeEmpty();
    }
}
