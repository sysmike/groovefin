namespace Groovefin.Core.Layout;

/// <summary>
/// Sizes the tiles of a grid page (albums, artists, genres, playlists) so that whole columns fill each row,
/// with the same margin on the right as on the left (#232).
/// </summary>
public static class TileGrid
{
    /// <summary>
    /// Kept free at the end of the row: the layout rounds each tile to whole pixels, which can add up to a
    /// pixel and push the last column into the next row.
    /// </summary>
    public const double RoundingSlack = 1;

    /// <summary>
    /// Gets the width of each tile when as many tiles of at least <paramref name="minWidth"/> as fit in
    /// <paramref name="available"/>, <paramref name="spacing"/> apart, grow to fill it. Whole pixels; at least
    /// <paramref name="minWidth"/>, which is also the answer before the grid has a width.
    /// </summary>
    public static double Fit(double available, double minWidth, double spacing)
    {
        var columns = Columns(available, minWidth, spacing);
        var room = available - RoundingSlack;
        if (double.IsNaN(room) || double.IsInfinity(room) || room <= minWidth)
        {
            return minWidth;
        }

        return Math.Max(minWidth, Math.Floor((room - (spacing * (columns - 1))) / columns));
    }

    /// <summary>
    /// Gets how many tiles of at least <paramref name="minWidth"/>, <paramref name="spacing"/> apart, fit in one
    /// row of <paramref name="available"/>: the columns of <see cref="Fit"/>. At least one, also before the
    /// grid has a width.
    /// </summary>
    public static int Columns(double available, double minWidth, double spacing)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(spacing);

        var room = available - RoundingSlack;
        if (double.IsNaN(room) || double.IsInfinity(room) || room <= minWidth)
        {
            return 1;
        }

        return (int)Math.Floor((room + spacing) / (minWidth + spacing));
    }
}
