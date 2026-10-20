using Groovefin.Audio;

namespace Groovefin.Core.Equalizer;

/// <summary>The equalizer: on or off, the chosen preset, the user's own bands and the preamp.</summary>
public sealed record EqualizerSettings
{
    public bool Enabled { get; set; }

    public EqualizerPreset Preset { get; set; } = EqualizerPreset.Flat;

    /// <summary>Gets or sets the band gains of <see cref="EqualizerPreset.Custom"/> in dB; kept while a preset is chosen.</summary>
    public IReadOnlyList<double> CustomGains { get; set; } = new double[EqualizerBands.Count];

    /// <summary>Gets or sets the gain before the bands in dB: below 0 leaves room for raised bands.</summary>
    public double Preamp { get; set; }

    /// <summary>Gets the band gains that apply: the preset's, or <see cref="CustomGains"/>. Always one per band, within the range.</summary>
    public IReadOnlyList<double> Gains => ToParameters().Gains;

    /// <summary>Gets what the audio effect applies.</summary>
    public EqualizerParameters ToParameters() =>
        new(Enabled, Preamp, EqualizerPresets.GainsOf(Preset) ?? CustomGains ?? []);

    public bool Equals(EqualizerSettings? other) =>
        other is not null && Enabled == other.Enabled && Preset == other.Preset && Preamp == other.Preamp
        && (CustomGains ?? []).SequenceEqual(other.CustomGains ?? []);

    public override int GetHashCode() => HashCode.Combine(Enabled, Preset, Preamp, (CustomGains ?? []).Count);
}
