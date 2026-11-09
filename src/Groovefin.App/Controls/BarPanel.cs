using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Groovefin.App.Controls;

// Public: XAML creates it and sets its properties at run time; no setters are generated for an internal class (#140).
/// <summary>
/// The bars of a statistics chart (#382): its children side by side, all as wide, <see cref="Spacing"/> apart, filling
/// the panel's width; as many as there are (7 days, a month's days, 12 months, 24 hours).
/// </summary>
public sealed partial class BarPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(BarPanel), new PropertyMetadata(2d, OnSpacingChanged));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = BarWidth(availableSize.Width);
        var height = 0d;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? (width + Spacing) * Children.Count : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = BarWidth(finalSize.Width);
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(i * (width + Spacing), 0, width, finalSize.Height));
        }

        return finalSize;
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BarPanel)d).InvalidateMeasure();

    /// <summary>Each bar's width: the panel's width shared out, at least one pixel; 8 pixels before it has a width.</summary>
    private double BarWidth(double available)
    {
        var count = Children.Count;
        if (count == 0 || double.IsInfinity(available) || double.IsNaN(available))
        {
            return 8;
        }

        return Math.Max(1, (available - (Spacing * (count - 1))) / count);
    }
}
