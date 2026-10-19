using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Groovefin.App.Controls;

/// <summary>
/// Connected animation of an album cover between a list (Albums page) and album detail, in both
/// directions. Nothing animates when the Windows "Animation effects" setting is off.
/// </summary>
internal static class CoverAnimation
{
    private const string ToDetailKey = "AlbumCoverToDetail";
    private const string ToListKey = "AlbumCoverToList";

    private static readonly UISettings UiSettings = new();

    private static bool AnimationsEnabled => UiSettings.AnimationsEnabled;

    private static ConnectedAnimationService Service => ConnectedAnimationService.GetForCurrentView();

    /// <summary>Captures the cover of <paramref name="item"/> in <paramref name="list"/> before opening album detail.</summary>
    public static void PrepareToDetail(FrameworkElement list, object item)
    {
        if (AnimationsEnabled && FindCover(list, item) is { } cover)
        {
            Service.PrepareToAnimate(ToDetailKey, cover);
        }
    }

    /// <summary>Flies the captured cover into <paramref name="cover"/> on album detail.</summary>
    public static void StartOnDetail(UIElement cover) => Start(ToDetailKey, cover);

    /// <summary>Captures the detail cover before going back to the list.</summary>
    public static void PrepareToList(UIElement cover)
    {
        if (AnimationsEnabled)
        {
            Service.PrepareToAnimate(ToListKey, cover);
        }
    }

    /// <summary>Flies the detail cover back into the tile of <paramref name="item"/>, if it is on screen.</summary>
    public static void StartOnList(FrameworkElement list, object? item)
    {
        if (Service.GetAnimation(ToListKey) is not { } animation)
        {
            return;
        }

        if (AnimationsEnabled && item is not null && FindCover(list, item) is { } cover)
        {
            animation.Configuration = new DirectConnectedAnimationConfiguration();
            animation.TryStart(cover);
        }
        else
        {
            animation.Cancel();
        }
    }

    private static void Start(string key, UIElement target)
    {
        if (Service.GetAnimation(key) is not { } animation)
        {
            return;
        }

        if (AnimationsEnabled)
        {
            animation.TryStart(target);
        }
        else
        {
            animation.Cancel();
        }
    }

    /// <summary>
    /// Finds the cover image of the realized tile showing <paramref name="item"/>. Only tiles inside the
    /// list's viewport count; recycled elements keep stale data and must not be used.
    /// </summary>
    private static Image? FindCover(FrameworkElement list, object item)
    {
        var viewport = new Rect(0, 0, list.ActualWidth, list.ActualHeight);
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(list);
        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            if (node is ItemContainer container && Equals(container.DataContext, item) && IsInside(container, list, viewport))
            {
                return FirstImage(container);
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                pending.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
        }

        return null;
    }

    private static bool IsInside(FrameworkElement element, FrameworkElement list, Rect viewport)
    {
        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0)
        {
            return false;
        }

        var bounds = element.TransformToVisual(list).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bounds.Intersect(viewport);
        return !bounds.IsEmpty;
    }

    private static Image? FirstImage(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var image = child as Image ?? FirstImage(child);
            if (image is not null)
            {
                return image;
            }
        }

        return null;
    }
}
