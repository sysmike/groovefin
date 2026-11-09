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

    /// <summary>
    /// Album gain while an album plays in order, track gain otherwise: in shuffle and mixed playlists (#393).
    /// </summary>
    Automatic,
}

/// <summary>How loud normalized music plays (#393): the server's target, or louder.</summary>
public enum NormalizationTarget
{
    /// <summary>The server's target loudness (about −18 LUFS): every track can be matched, but it is quiet.</summary>
    Server,

    /// <summary>4 dB louder: tracks quieter than the target stay a little quieter.</summary>
    Louder,

    /// <summary>7 dB louder: close to most streaming services, more quiet tracks stay quieter.</summary>
    Loudest,
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

    /// <summary>Gets the dB a <paramref name="target"/> adds to every gain before <see cref="MaxGainDb"/> applies.</summary>
    public static double PreampDb(NormalizationTarget target) => target switch
    {
        NormalizationTarget.Louder => 4,
        NormalizationTarget.Loudest => 7,
        _ => 0,
    };

    /// <summary>Gets the gain in dB that <paramref name="mode"/> applies to <paramref name="track"/>,
    /// or <see langword="null"/> when there is none. Album mode falls back to the track gain.
    /// <see cref="NormalizationMode.Automatic"/> needs the queue: on its own a track takes its track gain.</summary>
    public static double? GainDb(Track track, NormalizationMode mode, double preampDb = 0)
    {
        ArgumentNullException.ThrowIfNull(track);
        return Limit(mode switch
        {
            NormalizationMode.Track or NormalizationMode.Automatic => track.NormalizationGain,
            NormalizationMode.Album => track.AlbumNormalizationGain ?? track.NormalizationGain,
            _ => null,
        }, preampDb);
    }

    /// <summary>
    /// Gets the gain for the track at <paramref name="index"/> of <paramref name="order"/> (the queue in play order).
    /// Automatic takes the album gain when a neighbour is the album's track before or after it. Album gain is the
    /// same for the whole album, so a track the server did not analyse takes it from another track of its album in
    /// the queue rather than its own track gain, which would lose the album's dynamics.
    /// </summary>
    public static double? GainDb(IReadOnlyList<Track> order, int index, NormalizationMode mode, double preampDb = 0)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, order.Count);
        var track = order[index];
        var useAlbum = mode == NormalizationMode.Album || (mode == NormalizationMode.Automatic && PlaysInAlbumOrder(order, index));
        if (!useAlbum)
        {
            return GainDb(track, mode == NormalizationMode.Automatic ? NormalizationMode.Track : mode, preampDb);
        }

        var albumGain = track.AlbumNormalizationGain
            ?? (track.AlbumId is { } album ? order.FirstOrDefault(t => t.AlbumId == album && t.AlbumNormalizationGain is not null)?.AlbumNormalizationGain : null);
        return Limit(albumGain ?? track.NormalizationGain, preampDb);
    }

    /// <summary>Gets the volume factor (0 to 1) for <paramref name="track"/>; 1 without a gain.</summary>
    public static double VolumeFactor(Track track, NormalizationMode mode, double preampDb = 0) => Factor(GainDb(track, mode, preampDb));

    /// <summary>Gets the volume factor (0 to 1) for the track at <paramref name="index"/> of the play order.</summary>
    public static double VolumeFactor(IReadOnlyList<Track> order, int index, NormalizationMode mode, double preampDb = 0) =>
        Factor(GainDb(order, index, mode, preampDb));

    /// <summary>
    /// Whether the track plays as part of its album in order: the one before it is the same album's earlier track,
    /// or the one after it the same album's later track. In a shuffle the album's tracks rarely meet in order.
    /// </summary>
    internal static bool PlaysInAlbumOrder(IReadOnlyList<Track> order, int index)
    {
        var track = order[index];
        if (track.AlbumId is not { } album)
        {
            return false;
        }

        return (index > 0 && order[index - 1].AlbumId == album && Place(order[index - 1]).CompareTo(Place(track)) < 0)
            || (index + 1 < order.Count && order[index + 1].AlbumId == album && Place(order[index + 1]).CompareTo(Place(track)) > 0);
    }

    private static (int Disc, int Number) Place(Track track) => (track.DiscNumber ?? 1, track.TrackNumber ?? 0);

    private static double? Limit(double? gain, double preampDb) =>
        gain is { } value && double.IsFinite(value) ? Math.Min(value + preampDb, MaxGainDb) : null;

    private static double Factor(double? gain) => gain is { } value ? Math.Pow(10, value / 20) : 1;
}
