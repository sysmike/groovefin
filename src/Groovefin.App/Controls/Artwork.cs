using System.Runtime.InteropServices;
using Groovefin.Core.Images;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Groovefin.App.Controls;

/// <summary>
/// Attached property that loads artwork into an <see cref="Image"/> through the disk cache and an
/// in-memory cache of decoded bitmaps:
/// <c>&lt;Image controls:Artwork.Request="{x:Bind ViewModel.Cover}" /&gt;</c>.
/// Works with virtualized lists: a recycled container only shows the image of its latest request.
/// </summary>
internal static class Artwork
{
    private const int DecodedCapacity = 300;

    private static readonly LruCache<string, BitmapImage> Decoded = new(DecodedCapacity);

    public static readonly DependencyProperty RequestProperty = DependencyProperty.RegisterAttached(
        "Request", typeof(ImageRequest), typeof(Artwork), new PropertyMetadata(null, OnRequestChanged));

    public static ImageRequest? GetRequest(Image image) => (ImageRequest?)image.GetValue(RequestProperty);

    public static void SetRequest(Image image, ImageRequest? value) => image.SetValue(RequestProperty, value);

    /// <summary>Drops decoded bitmaps, e.g. after the disk cache was cleared.</summary>
    public static void ClearMemoryCache() => Decoded.Clear();

    private static async void OnRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image)
        {
            return;
        }

        image.Source = null;
        if (e.NewValue is not ImageRequest request)
        {
            return;
        }

        var cache = App.Services.GetRequiredService<IImageCache>();
        var path = await cache.GetAsync(request);

        // The container may have been recycled for another item while we waited.
        if (path is null || !ReferenceEquals(GetRequest(image), request))
        {
            return;
        }

        if (!Decoded.TryGet(path, out var bitmap))
        {
            bitmap = new BitmapImage { DecodePixelWidth = request.BucketWidth, DecodePixelType = DecodePixelType.Physical };
            try
            {
                // Opened asynchronously: this runs on the UI thread for every cover that scrolls into view.
                using var stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
                await bitmap.SetSourceAsync(stream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
            {
                // A truncated or evicted file: show the placeholder; the next request downloads again.
                return;
            }

            Decoded.Set(path, bitmap);
        }

        if (ReferenceEquals(GetRequest(image), request))
        {
            image.Source = bitmap;
        }
    }
}
