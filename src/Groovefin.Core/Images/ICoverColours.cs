namespace Groovefin.Core.Images;

/// <summary>Gets a cover's colour (#367); the app decodes the cached image.</summary>
public interface ICoverColours
{
    /// <summary>Gets the cover's colour (<see cref="CoverColour.Pick"/>), or <see langword="null"/> when it can't be loaded.</summary>
    Task<CoverColour?> GetAsync(ImageRequest request, CancellationToken cancellationToken = default);
}
