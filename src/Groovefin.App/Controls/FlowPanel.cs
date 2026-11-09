using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Groovefin.App.Controls;

// Public: XAML creates it and sets its properties at run time; no setters are generated for an internal class (#140).
/// <summary>
/// Its children left to right at their own size, <see cref="HorizontalSpacing"/> apart, starting a new line, <see
/// cref="VerticalSpacing"/> below, when the next one would not fit (the Settings page's section links, #404).
/// </summary>
public sealed partial class FlowPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(FlowPanel), new PropertyMetadata(0d, OnSpacingChanged));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(FlowPanel), new PropertyMetadata(0d, OnSpacingChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }

        return Layout(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, arrange: true);
        return finalSize;
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((FlowPanel)d).InvalidateMeasure();

    /// <summary>Places the children in lines no wider than <paramref name="width"/>; returns the size they take.</summary>
    private Size Layout(double width, bool arrange)
    {
        double x = 0, y = 0, lineHeight = 0, widest = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                // No place and no spacing.
                continue;
            }

            var size = child.DesiredSize;

            if (x > 0 && x + size.Width > width)
            {
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }

            if (arrange)
            {
                child.Arrange(new Rect(x, y, size.Width, size.Height));
            }

            widest = Math.Max(widest, x + size.Width);
            x += size.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return new Size(double.IsInfinity(width) ? widest : Math.Min(widest, width), y + lineHeight);
    }
}
