using Groovefin.Core.Layout;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Tests.Layout;

public sealed class WindowPlacementTests
{
    // Two 1920 × 1080 screens side by side at 100 %, the taskbar 48 px high; the primary one on the left.
    private static readonly ScreenArea Left = new(new PixelRect(0, 0, 1920, 1032), 96, IsPrimary: true);
    private static readonly ScreenArea Right = new(new PixelRect(1920, 0, 1920, 1032), 96, IsPrimary: false);
    private static readonly ScreenArea[] BothScreens = [Left, Right];

    private static readonly PixelRect Normal = new(100, 80, 1200, 800);

    private static WindowSettings Saved(int x, int y, int width, int height, int dpi = 96, bool maximized = false) =>
        new() { X = x, Y = y, Width = width, Height = height, Dpi = dpi, IsMaximized = maximized };

    [Fact]
    public void Nothing_is_saved_before_the_full_window_was_seen()
    {
        new WindowPlacement().ToSettings().Should().BeNull();
    }

    [Fact]
    public void The_full_windows_last_bounds_are_saved()
    {
        var sut = new WindowPlacement();
        sut.Observe(true, WindowShowState.Normal, new PixelRect(0, 0, 900, 700), 96);
        sut.Observe(true, WindowShowState.Normal, Normal, 120);

        sut.ToSettings().Should().Be(Saved(100, 80, 1200, 800, dpi: 120));
    }

    [Fact]
    public void Maximized_keeps_the_size_before_so_un_maximizing_gives_it_back()
    {
        var sut = new WindowPlacement();
        sut.Observe(true, WindowShowState.Normal, Normal, 96);
        sut.Observe(true, WindowShowState.Maximized, new PixelRect(-8, -8, 1936, 1048), 96);

        sut.ToSettings().Should().Be(Saved(100, 80, 1200, 800, maximized: true));
    }

    [Fact]
    public void Minimized_the_mini_player_and_full_screen_do_not_change_what_is_saved()
    {
        var sut = new WindowPlacement();
        sut.Observe(true, WindowShowState.Normal, Normal, 96);

        sut.Observe(true, WindowShowState.Minimized, new PixelRect(-32000, -32000, 160, 28), 96);
        sut.Observe(false, WindowShowState.Normal, new PixelRect(1500, 900, 420, 140), 96);
        sut.Observe(false, WindowShowState.Normal, new PixelRect(0, 0, 1920, 1080), 96);

        sut.ToSettings().Should().Be(Saved(100, 80, 1200, 800));
    }

    [Fact]
    public void A_saved_place_still_on_a_screen_is_kept()
    {
        WindowPlacement.Restore(Saved(2100, 50, 1200, 800), BothScreens, 860, 600)
            .Should().Be(new PixelRect(2100, 50, 1200, 800));
    }

    [Fact]
    public void A_window_on_a_screen_that_is_gone_opens_centred_on_the_primary_one()
    {
        WindowPlacement.Restore(Saved(2100, 50, 1200, 800), [Left], 860, 600)
            .Should().Be(new PixelRect(360, 116, 1200, 800));
    }

    [Fact]
    public void A_window_partly_off_its_screen_is_moved_fully_onto_it()
    {
        // About 70 % on the left screen: past its right edge and below the taskbar.
        WindowPlacement.Restore(Saved(1400, 300, 600, 900), [Left], 400, 300)
            .Should().Be(new PixelRect(1320, 132, 600, 900));
    }

    [Fact]
    public void The_size_follows_a_changed_scale_and_stays_within_the_minimum_and_the_screen()
    {
        var at150 = new ScreenArea(new PixelRect(0, 0, 2880, 1548), 144, IsPrimary: true);

        WindowPlacement.Restore(Saved(100, 80, 1200, 800), [at150], 860, 600)
            .Should().Be(new PixelRect(100, 80, 1800, 1200));
        WindowPlacement.Restore(Saved(0, 0, 400, 300), [at150], 860, 600)
            .Should().Be(new PixelRect(0, 0, 1290, 900), "the minimum is 860 × 600 effective pixels");
        WindowPlacement.Restore(Saved(0, 0, 5000, 3000), [Left], 860, 600)
            .Should().Be(new PixelRect(0, 0, 1920, 1032), "no larger than the work area");
    }
}
