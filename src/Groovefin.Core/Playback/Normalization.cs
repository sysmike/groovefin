using Groovefin.Core.Models;

namespace Groovefin.Core.Playback;

/// <summary>Which loudness gain playback applies.</summary>
public enum NormalizationMode
{
    /// <summary>Tracks play as they are.</summary>
    Off,

    /// <summary>Every track at the same loudness.</summary>
    Track,

    /// <summary>Albums at the same loudness, keeping the differences between their tracks.</summary>
    Album,
}

/// <summary>
/// Turns the server's loudness analysis (<see cref="Track.NormalizationGain"/>,
/// <see cref="Track.AlbumNormalizationGain"/>) into a volume factor. Gains are limited to
/// <see cref="MaxGainDb"/>: the player's volume cannot go above 100 %, and amplifying would clip.
/// </summary>
public static class Normalization
{
    /// <summary>The largest gain applied; quiet tracks stay as they are rather than clip.</summary>
    public const double MaxGainDb = 0;

    /// <summary>Gets the gain in dB that <paramref name="mode"/> applies to <paramref name="track"/>,
    /// or <see langword="null"/> when there is none. Album mode falls back to the track gain.</summary>
    public static double? GainDb(Track track, NormalizationMode mode)
    {
        ArgumentNullException.ThrowIfNull(track);

        var gain = mode switch
        {
            NormalizationMode.Track => track.NormalizationGain,
            NormalizationMode.Album => track.AlbumNormalizationGain ?? track.NormalizationGain,
            _ => null,
        };

        return gain is { } value && double.IsFinite(value) ? Math.Min(value, MaxGainDb) : null;
    }

    /// <summary>Gets the volume factor (0 to 1) for <paramref name="track"/>; 1 without a gain.</summary>
    public static double VolumeFactor(Track track, NormalizationMode mode) =>
        GainDb(track, mode) is { } gain ? Math.Pow(10, gain / 20) : 1;
}
