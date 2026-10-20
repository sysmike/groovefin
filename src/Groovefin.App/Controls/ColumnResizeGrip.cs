using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Groovefin.App.Controls;

// Public: XAML sets these properties at run time and generates no setter for an internal class (#140).
/// <summary>
/// The draggable right edge of a table column header: a thin line with the left–right resize cursor.
/// It reports how far it was dragged; keyboard users resize from the header's context menu.
/// </summary>
public sealed partial class ColumnResizeGrip : Grid
{
    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(ColumnResizeGrip), new PropertyMetadata(null, OnLineBrushChanged));

    private readonly Rectangle _line = new() { Width = 1, HorizontalAlignment = HorizontalAlignment.Center };
    private double? _startX;

    public ColumnResizeGrip()
    {
        // A transparent background makes the whole width hit-testable, not only the line.
        Background = new SolidColorBrush(Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        Children.Add(_line);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerEnded;
        PointerCaptureLost += OnPointerEnded;
    }

    /// <summary>Raised when a drag starts.</summary>
    public event EventHandler? ResizeStarted;

    /// <summary>Raised while dragging, with the distance from the start in device-independent pixels.</summary>
    public event EventHandler<double>? Resizing;

    /// <summary>Raised when the drag ends.</summary>
    public event EventHandler? ResizeCompleted;

    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    private static void OnLineBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColumnResizeGrip)d)._line.Fill = e.NewValue as Brush;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(null);
        if (point.Properties.IsLeftButtonPressed && CapturePointer(e.Pointer))
        {
            // Window coordinates: the grip itself moves while the column changes width.
            _startX = point.Position.X;
            ResizeStarted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_startX is { } start)
        {
            Resizing?.Invoke(this, e.GetCurrentPoint(null).Position.X - start);
            e.Handled = true;
        }
    }

    private void OnPointerEnded(object sender, PointerRoutedEventArgs e)
    {
        if (_startX is null)
        {
            return;
        }

        _startX = null;
        ReleasePointerCaptures();
        ResizeCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
