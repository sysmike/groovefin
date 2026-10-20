using Groovefin.Audio;

namespace Groovefin.Core.Equalizer;

/// <summary>The equalizer presets, in the order of the preset list; <see cref="Custom"/> is the user's own bands.</summary>
public enum EqualizerPreset
{
    Flat,
    BassBoost,
    TrebleBoost,
    Vocal,
    Rock,
    Pop,
    Jazz,
    Classical,
    Electronic,
    Loudness,
    Custom,
}

/// <summary>The band gains of each preset, in dB for <see cref="EqualizerBands.Frequencies"/> (31 Hz to 16 kHz).</summary>
public static class EqualizerPresets
{
    private static readonly Dictionary<EqualizerPreset, double[]> Gains = new()
    {
        [EqualizerPreset.Flat] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [EqualizerPreset.BassBoost] = [6, 5, 4, 2, 0, 0, 0, 0, 0, 0],
        [EqualizerPreset.TrebleBoost] = [0, 0, 0, 0, 0, 1, 2, 4, 5, 6],
        [EqualizerPreset.Vocal] = [-2, -2, -1, 1, 3, 3, 2, 1, 0, -1],
        [EqualizerPreset.Rock] = [4, 3, 2, 0, -1, -1, 1, 2, 3, 4],
        [EqualizerPreset.Pop] = [-1, 1, 3, 4, 3, 1, -1, -1, -1, -1],
        [EqualizerPreset.Jazz] = [3, 2, 1, 2, -1, -1, 0, 1, 2, 3],
        [EqualizerPreset.Classical] = [0, 0, 0, 0, 0, 0, -3, -3, -3, -4],
        [EqualizerPreset.Electronic] = [5, 4, 1, 0, -2, 2, 1, 1, 4, 5],
        [EqualizerPreset.Loudness] = [5, 4, 2, 0, -1, 0, 0, 1, 3, 4],
    };

    /// <summary>Gets every preset in list order, <see cref="EqualizerPreset.Custom"/> last.</summary>
    public static IReadOnlyList<EqualizerPreset> All { get; } = Enum.GetValues<EqualizerPreset>();

    /// <summary>Gets the band gains of <paramref name="preset"/>; <see langword="null"/> for <see cref="EqualizerPreset.Custom"/>.</summary>
    public static IReadOnlyList<double>? GainsOf(EqualizerPreset preset) => Gains.GetValueOrDefault(preset);
}
