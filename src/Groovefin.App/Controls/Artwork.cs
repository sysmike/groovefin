using System.Runtime.InteropServices;
using Groovefin.Core.Images;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Groovefin.App.Controls;

// Public: XAML sets these properties at run time and generates no setter for an internal class (#140).
/// <summary>
/// Attached property that loads artwork into an <see cref="Image"/> through the disk cache and an
/// in-memory cache of decoded bitmaps:
/// <c>&lt;Image controls:Artwork.Request="{x:Bind ViewModel.Cover}" /&gt;</c>.
/// Works with virtualized lists: a recycled container only shows the image of its latest request.
/// </summary>
public static class Artwork
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

        var bitmap = await LoadAsync(request);

        // The container may have been recycled for another item while we waited.
        if (bitmap is not null && ReferenceEquals(GetRequest(image), request))
        {
            image.Source = bitmap;
        }
    }

    /// <summary>
    /// Gets the decoded artwork, for sources that are not an <see cref="Image"/> (an <see cref="ImageIcon"/>, #299);
    /// <see langword="null"/> when it cannot be loaded. Call it on the UI thread.
    /// </summary>
    public static async Task<BitmapImage?> LoadAsync(ImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var cache = App.Services.GetRequiredService<IImageCache>();
        if (await cache.GetAsync(request) is not { } path)
        {
            return null;
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
                return null;
            }

            Decoded.Set(path, bitmap);
        }

        return bitmap;
    }
}
