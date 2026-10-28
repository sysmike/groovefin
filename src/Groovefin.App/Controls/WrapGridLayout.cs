using Groovefin.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Groovefin.App.Controls;

// Public: XAML creates it and sets its properties at run time; no setters are generated for an internal class (#140).
/// <summary>
/// A grid of equal-width tiles in rows, like <see cref="UniformGridLayout"/>, for a short grid inside a
/// scrolling page (the artist page's albums and similar artists, #263, #264). It creates every tile, also
/// while the grid is off screen: a virtualizing layout there creates none until it is scrolled into view,
/// so Tab could not reach the grid. <see cref="TileSize"/> sets <see cref="MinItemWidth"/> as for a
/// <see cref="UniformGridLayout"/>; each row is as high as its highest tile.
/// </summary>
public sealed partial class WrapGridLayout : NonVirtualizingLayout
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(WrapGridLayout), new PropertyMetadata(1d, OnLayoutChanged));

    public static readonly DependencyProperty MinColumnSpacingProperty = DependencyProperty.Register(
        nameof(MinColumnSpacing), typeof(double), typeof(WrapGridLayout), new PropertyMetadata(0d, OnLayoutChanged));

    public static readonly DependencyProperty MinRowSpacingProperty = DependencyProperty.Register(
        nameof(MinRowSpacing), typeof(double), typeof(WrapGridLayout), new PropertyMetadata(0d, OnLayoutChanged));

    /// <summary>Gets or sets the width of each tile.</summary>
    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double MinColumnSpacing
    {
        get => (double)GetValue(MinColumnSpacingProperty);
        set => SetValue(MinColumnSpacingProperty, value);
    }

    public double MinRowSpacing
    {
        get => (double)GetValue(MinRowSpacingProperty);
        set => SetValue(MinRowSpacingProperty, value);
    }

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var children = context.Children;
        var columns = Columns(availableSize.Width);
        var height = 0d;
        var rowHeight = 0d;
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(new Size(MinItemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);
            if (i % columns == columns - 1 || i == children.Count - 1)
            {
                height += rowHeight + (i >= columns ? MinRowSpacing : 0);
                rowHeight = 0;
            }
        }

        var used = Math.Min(columns, children.Count);
        var width = used > 0 ? (used * MinItemWidth) + ((used - 1) * MinColumnSpacing) : 0;
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        var columns = Columns(finalSize.Width);
        var y = 0d;
        for (var row = 0; row * columns < children.Count; row++)
        {
            var first = row * columns;
            var last = Math.Min(first + columns, children.Count);
            var rowHeight = 0d;
            for (var i = first; i < last; i++)
            {
                rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);
            }

            for (var i = first; i < last; i++)
            {
                var x = (i - first) * (MinItemWidth + MinColumnSpacing);
                children[i].Arrange(new Rect(x, y, MinItemWidth, rowHeight));
            }

            y += rowHeight + MinRowSpacing;
        }

        return finalSize;
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WrapGridLayout)d).InvalidateMeasure();

    private int Columns(double available) => TileGrid.Columns(available, Math.Max(1, MinItemWidth), MinColumnSpacing);
}
