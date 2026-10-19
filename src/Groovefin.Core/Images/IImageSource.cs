namespace Groovefin.Core.Images;

/// <summary>Downloads images from the active server. Implemented by the Jellyfin layer.</summary>
public interface IImageSource
{
    /// <summary>Gets an identifier of the active server, so caches of different servers never mix.</summary>
    /// <returns>The identifier, or <see langword="null"/> when not connected.</returns>
    string? ServerKey { get; }

    /// <summary>Downloads the image, resized by the server to <see cref="ImageRequest.BucketWidth"/>.</summary>
    /// <exception cref="Api.JellyfinException">The download failed.</exception>
    Task<Stream> OpenAsync(ImageRequest request, CancellationToken cancellationToken);
}
