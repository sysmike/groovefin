namespace Groovefin.Core.Images;

/// <summary>An image to show when the server cannot give one, such as a cover kept with the downloads (#335).</summary>
public interface IImageFallback
{
    /// <summary>Gets the path of a file to show for <paramref name="request"/>, or <see langword="null"/>.</summary>
    string? Find(ImageRequest request);
}
