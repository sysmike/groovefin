using System.Runtime.InteropServices;
using Groovefin.Core.Images;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Groovefin.App.Services;

/// <summary>Takes a cover's colour from the cached image, decoded to a small copy (#367).</summary>
internal sealed class CoverColourSource(IImageCache cache) : ICoverColours
{
    /// <summary>The copy's width and height: enough to find the main colour, quick to decode.</summary>
    private const uint SampleSize = 32;

    public async Task<CoverColour?> GetAsync(ImageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await cache.GetAsync(request, cancellationToken) is not { } path)
        {
            return null;
        }

        try
        {
            using var stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform
            {
                ScaledWidth = SampleSize,
                ScaledHeight = SampleSize,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return CoverColour.Pick(pixels.DetachPixelData());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            // A truncated or evicted file: the card keeps its plain background.
            return null;
        }
    }
}
