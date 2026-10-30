using Groovefin.App.Services;
using Groovefin.Core.Models;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Groovefin.App.Controls;

// Public: XAML sets these properties at run time and generates no setter for an internal class (#140).
/// <summary>
/// Lets a list of tracks select several, as in Windows (#295): <c>controls:TrackSelection.IsEnabled="True"</c>
/// on a <see cref="ListView"/>. Ctrl+click adds or removes a track, Shift+click selects a range, and the
/// list's own keys work (Shift+arrows, Ctrl+Space, Ctrl+A); Esc clears the selection. A plain click still
/// plays (the page's <c>ItemClick</c> skips <see cref="IsSelecting"/>) and selects the track, so Shift+click
/// has a start. The context menu (<see cref="ItemMenu"/>) and dragging (<see cref="TrackDragSource"/>) act
/// on the selection. The lists inside an element with <c>TrackSelection.IsScope="True"</c> (an album's
/// discs) share one selection: a click without Ctrl in one clears the others.
/// </summary>
public static class TrackSelection
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(TrackSelection), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IsScopeProperty = DependencyProperty.RegisterAttached(
        "IsScope", typeof(bool), typeof(TrackSelection), new PropertyMetadata(false));

    public static bool GetIsEnabled(UIElement element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(UIElement element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsScope(UIElement element) => (bool)element.GetValue(IsScopeProperty);

    public static void SetIsScope(UIElement element, bool value) => element.SetValue(IsScopeProperty, value);

    /// <summary>Gets a value indicating whether Ctrl or Shift is held: the click selects and does not play.</summary>
    public static bool IsSelecting => KeyboardInput.IsDown(VirtualKey.Control) || KeyboardInput.IsDown(VirtualKey.Shift);

    /// <summary>Gets the list with selection enabled that <paramref name="element"/> is in, or <see langword="null"/>.</summary>
    public static ListViewBase? ListOf(DependencyObject? element)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListViewBase list)
            {
                return GetIsEnabled(list) ? list : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets what a context menu on <paramref name="item"/> acts on: the selection when the item is part of it,
    /// in list order. An item outside the selection becomes the selection first, as in Explorer.
    /// </summary>
    public static IReadOnlyList<object> ForMenu(ListViewBase list, object item)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(item);
        if (!list.SelectedItems.Contains(item))
        {
            foreach (var other in Scope(list))
            {
                other.SelectedItems.Clear();
            }

            list.SelectedItems.Add(item);
        }

        return Selected(list);
    }

    /// <summary>Gets the selected items of the list's scope, in list order (the lists' order, then each list's).</summary>
    public static IReadOnlyList<object> Selected(ListViewBase list)
    {
        ArgumentNullException.ThrowIfNull(list);
        var items = new List<object>();
        foreach (var member in Scope(list))
        {
            foreach (var range in member.SelectedRanges.OrderBy(r => r.FirstIndex))
            {
                for (var i = range.FirstIndex; i <= range.LastIndex && i < member.Items.Count; i++)
                {
                    items.Add(member.Items[i]);
                }
            }
        }

        return items;
    }

    /// <summary>Gets the tracks of <paramref name="items"/>; <see langword="null"/> when one of them is not a track.</summary>
    public static IReadOnlyList<Track>? TracksOf(IEnumerable<object> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var tracks = new List<Track>();
        foreach (var item in items)
        {
            if (TrackOf(item) is not { } track)
            {
                return null;
            }

            tracks.Add(track);
        }

        return tracks;
    }

    /// <summary>Gets the track a list row shows, or <see langword="null"/> for other rows.</summary>
    public static Track? TrackOf(object? item) => item switch
    {
        Track track => track,
        TrackItem row => row.Track,
        ArtistTrackItem row => row.Track,
        SearchTrackItem row => row.Track,
        FavouriteTrackItem row => row.Track,
        PlaylistEntryItem row => row.Entry.Track,
        OnRepeatItem row => row.Track,
        TrackRow row => row.Track,
        SongRow row => row.Track,
        _ => null,
    };

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListViewBase list)
        {
            return;
        }

        list.KeyDown -= OnKeyDown;
        list.SelectionChanged -= OnSelectionChanged;
        if ((bool)e.NewValue)
        {
            list.SelectionMode = ListViewSelectionMode.Extended;
            list.KeyDown += OnKeyDown;
            list.SelectionChanged += OnSelectionChanged;
        }
        else
        {
            list.SelectionMode = ListViewSelectionMode.None;
        }
    }

    /// <summary>Esc clears the selection; with nothing selected it goes on to the window (full screen, mini player).</summary>
    private static void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape || sender is not ListViewBase list)
        {
            return;
        }

        foreach (var member in Scope(list).Where(m => m.SelectedItems.Count > 0))
        {
            member.SelectedItems.Clear();
            e.Handled = true;
        }
    }

    /// <summary>In a scope, selecting without Ctrl in one list clears the others, as one list would.</summary>
    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || sender is not ListViewBase list || KeyboardInput.IsDown(VirtualKey.Control))
        {
            return;
        }

        foreach (var other in Scope(list).Where(m => !ReferenceEquals(m, list)))
        {
            other.SelectedItems.Clear();
        }
    }

    /// <summary>The lists that share the selection with <paramref name="list"/>, in order; just the list without a scope.</summary>
    private static List<ListViewBase> Scope(ListViewBase list)
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(list); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement element && GetIsScope(element))
            {
                var members = new List<ListViewBase>();
                Collect(node, members);
                return members;
            }
        }

        return [list];
    }

    /// <summary>The selectable lists below <paramref name="node"/>, depth first, so in the order they are shown.</summary>
    private static void Collect(DependencyObject node, List<ListViewBase> lists)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is ListViewBase list)
            {
                if (GetIsEnabled(list))
                {
                    lists.Add(list);
                }
            }
            else
            {
                Collect(child, lists);
            }
        }
    }
}
