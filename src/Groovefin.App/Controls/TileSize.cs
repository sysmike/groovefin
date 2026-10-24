using Groovefin.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

// Public: XAML creates it and sets its properties at run time; no setters are generated for an internal class (#140).
/// <summary>
/// The width of the tiles in one grid, for its tile template to bind to. The tiles grow until whole columns
/// fill each row, so the margin on the right is as wide as on the left (#232):
/// <code>
/// &lt;controls:TileSize x:Key="AlbumTile" MinWidth="168" /&gt;
/// &lt;StackPanel Width="{Binding Width, Source={StaticResource AlbumTile}}"&gt;
/// &lt;ItemsView controls:TileSize.Source="{StaticResource AlbumTile}"&gt;
/// </code>
/// The ItemsView sets <see cref="Width"/>, and its <see cref="UniformGridLayout.MinItemWidth"/>, when it
/// resizes or gets another layout (the list layout of Albums and Artists is left alone).
/// </summary>
public sealed partial class TileSize : DependencyObject
{
    public static readonly DependencyProperty WidthProperty = DependencyProperty.Register(
        nameof(Width), typeof(double), typeof(TileSize), new PropertyMetadata(0d, OnWidthChanged));

    public static readonly DependencyProperty RoundProperty = DependencyProperty.Register(
        nameof(Round), typeof(CornerRadius), typeof(TileSize), new PropertyMetadata(default(CornerRadius)));

    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(TileSize), typeof(TileSize), new PropertyMetadata(null, OnSourceChanged));

    private double _minWidth;

    /// <summary>Gets or sets the least width of a tile: its width before the grid has one.</summary>
    public double MinWidth
    {
        get => _minWidth;
        set
        {
            _minWidth = value;
            if (Width < value)
            {
                Width = value;
            }
        }
    }

    /// <summary>Gets the width of each tile, and the height of a square cover.</summary>
    public double Width
    {
        get => (double)GetValue(WidthProperty);
        private set => SetValue(WidthProperty, value);
    }

    /// <summary>Gets the corner radius that makes a square of <see cref="Width"/> round (artist images).</summary>
    public CornerRadius Round => (CornerRadius)GetValue(RoundProperty);

    public static TileSize? GetSource(ItemsView view) => (TileSize?)view.GetValue(SourceProperty);

    public static void SetSource(ItemsView view, TileSize? value) => view.SetValue(SourceProperty, value);

    private static void OnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        d.SetValue(RoundProperty, new CornerRadius((double)e.NewValue / 2));

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsView view || e.OldValue is not null)
        {
            // Set once, in XAML.
            return;
        }

        view.SizeChanged += (_, _) => Fit(view);
        view.RegisterPropertyChangedCallback(ItemsView.LayoutProperty, (_, _) => Fit(view));
        Fit(view);
    }

    private static void Fit(ItemsView view)
    {
        if (GetSource(view) is not { MinWidth: > 0 } size || view.Layout is not UniformGridLayout layout)
        {
            return;
        }

        var available = view.ActualWidth - view.Padding.Left - view.Padding.Right;
        size.Width = TileGrid.Fit(available, size.MinWidth, layout.MinColumnSpacing);
        layout.MinItemWidth = size.Width;
    }
}
