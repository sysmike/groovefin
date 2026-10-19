using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Groovefin.App.Controls;

/// <summary>
/// Makes tracks and albums draggable onto the queue panel and the playlists in the navigation pane:
/// <c>controls:TrackDragSource.IsEnabled="True"</c> on a <see cref="ListView"/> of tracks (the selected
/// rows are dragged) or on an album tile. Context menus offer the same without a mouse.
/// </summary>
internal static class TrackDragSource
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(TrackDragSource), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(UIElement element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(UIElement element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Gets the token of a drag started here, or <see langword="null"/> for other drag data.</summary>
    public static async Task<string?> TokenAsync(DataPackageView data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Contains(TrackDrag.DataFormat) ? await data.GetDataAsync(TrackDrag.DataFormat) as string : null;
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var enabled = (bool)e.NewValue;
        if (d is ListViewBase list)
        {
            list.DragItemsStarting -= OnDragItemsStarting;
            if (enabled)
            {
                list.CanDragItems = true;
                list.DragItemsStarting += OnDragItemsStarting;
            }
        }
        else if (d is UIElement element)
        {
            element.CanDrag = enabled;
            element.DragStarting -= OnDragStarting;
            if (enabled)
            {
                element.DragStarting += OnDragStarting;
            }
        }
    }

    /// <summary>A list may also reorder its rows (a playlist): the drag then both moves and copies.</summary>
    private static void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (Begin(e.Items) is { } token)
        {
            e.Data.SetData(TrackDrag.DataFormat, token);
            e.Data.RequestedOperation |= DataPackageOperation.Copy;
        }
    }

    private static void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is { } item && Begin([item]) is { } token)
        {
            args.Data.SetData(TrackDrag.DataFormat, token);
            args.Data.RequestedOperation = DataPackageOperation.Copy;
        }
        else
        {
            args.Cancel = true;
        }
    }

    /// <summary>Starts a drag of the tracks or the one album in <paramref name="items"/>; <see langword="null"/> when they are neither.</summary>
    private static string? Begin(IList<object> items)
    {
        var drag = App.Services.GetRequiredService<TrackDrag>();
        if (items is [AlbumItem album])
        {
            return drag.BeginAlbum(album.Album);
        }

        var tracks = new List<Track>(items.Count);
        foreach (var item in items)
        {
            if (TrackOf(item) is not { } track)
            {
                return null;
            }

            tracks.Add(track);
        }

        return tracks.Count > 0 ? drag.Begin(tracks) : null;
    }

    private static Track? TrackOf(object item) => item switch
    {
        Track track => track,
        TrackItem row => row.Track,
        ArtistTrackItem row => row.Track,
        SearchTrackItem row => row.Track,
        FavouriteTrackItem row => row.Track,
        PlaylistEntryItem row => row.Entry.Track,
        TrackRow row => row.Track,
        SongRow row => row.Track,
        _ => null,
    };
}
