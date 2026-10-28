using Groovefin.Core.Layout;

namespace Groovefin.Core.Tests.Layout;

public sealed class TileGridTests
{
    [Fact]
    public void Tiles_grow_until_whole_columns_fill_the_row()
    {
        // The Albums page in the report: 1,640 px for tiles of at least 168 px, 16 px apart. Fixed tiles made
        // 8 columns of 1,456 px; grown to 190 px, the 8 columns take 1,632 px.
        var width = TileGrid.Fit(1640, 168, 16);

        width.Should().Be(190);
        ((8 * width) + (7 * 16)).Should().BeLessThanOrEqualTo(1640 - TileGrid.RoundingSlack);
    }

    [Theory]
    [InlineData(1000, 168, 16)]
    [InlineData(1640, 160, 16)]
    [InlineData(1640, 200, 12)]
    [InlineData(733.5, 168, 16)]
    [InlineData(2560, 168, 16)]
    public void The_row_fills_to_within_a_pixel_per_column(double available, double minWidth, double spacing)
    {
        var width = TileGrid.Fit(available, minWidth, spacing);

        var columns = Math.Floor((available - TileGrid.RoundingSlack + spacing) / (width + spacing));
        var row = (columns * width) + ((columns - 1) * spacing);
        width.Should().BeGreaterThanOrEqualTo(minWidth);
        row.Should().BeLessThanOrEqualTo(available - TileGrid.RoundingSlack);
        row.Should().BeGreaterThan(available - TileGrid.RoundingSlack - columns);
    }

    [Fact]
    public void More_room_adds_a_column_once_a_tile_of_the_least_width_fits()
    {
        // 4 columns of 168 px need 720 px plus the slack; 1 px less makes 3 columns of 229 px.
        TileGrid.Fit(721, 168, 16).Should().Be(168);
        TileGrid.Fit(720, 168, 16).Should().Be(229);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Without_room_for_one_tile_the_least_width_stays(double available)
    {
        TileGrid.Fit(available, 168, 16).Should().Be(168);
    }

    [Theory]
    [InlineData(1640, 168, 16, 8)]
    [InlineData(721, 168, 16, 4)]
    [InlineData(720, 168, 16, 3)]
    [InlineData(722, 168, 16, 4)]
    [InlineData(2560, 152, 16, 15)]
    public void Columns_counts_the_tiles_of_one_row(double available, double minWidth, double spacing, int expected)
    {
        var columns = TileGrid.Columns(available, minWidth, spacing);

        columns.Should().Be(expected);
        var width = TileGrid.Fit(available, minWidth, spacing);
        ((columns * width) + ((columns - 1) * spacing)).Should().BeLessThanOrEqualTo(available - TileGrid.RoundingSlack);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Without_room_for_two_tiles_one_column_stays(double available)
    {
        TileGrid.Columns(available, 168, 16).Should().Be(1);
    }
}
