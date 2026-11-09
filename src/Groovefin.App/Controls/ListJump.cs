using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Groovefin.App.Controls;

/// <summary>Shows the item a letter jump arrived at (#390): scrolled to the top of the list, with the focus on it.</summary>
internal static class ListJump
{
    public static void Show(ItemsView view, int index)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.StartBringItemIntoView(index, new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });

        // The item exists once the list has laid out the new place.
        view.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (Descendant<ItemsRepeater>(view)?.TryGetElement(index) is Control item)
            {
                item.Focus(FocusState.Keyboard);
            }
        });
    }

    public static void Show(ListViewBase list, int index)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (index < 0 || index >= list.Items.Count)
        {
            return;
        }

        list.ScrollIntoView(list.Items[index], ScrollIntoViewAlignment.Leading);
        list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (list.ContainerFromIndex(index) is Control item)
            {
                item.Focus(FocusState.Keyboard);
            }
        });
    }

    private static T? Descendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found)
            {
                return found;
            }

            if (Descendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}
