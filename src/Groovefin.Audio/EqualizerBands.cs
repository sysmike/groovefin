namespace Groovefin.Audio;

/// <summary>The ten bands of the graphic equalizer: one octave each, at the ISO centre frequencies.</summary>
public static class EqualizerBands
{
    /// <summary>The lowest gain of a band and of the preamp, in dB.</summary>
    public const double MinGain = -12;

    /// <summary>The highest gain of a band and of the preamp, in dB.</summary>
    public const double MaxGain = 12;

    /// <summary>The quality factor of each band's filter: one octave wide.</summary>
    public static readonly double Q = Math.Sqrt(2);

    /// <summary>Gets the centre frequencies in Hz, lowest first.</summary>
    public static IReadOnlyList<double> Frequencies { get; } = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>Gets the number of bands.</summary>
    public static int Count => Frequencies.Count;

    /// <summary>Limits a gain to <see cref="MinGain"/> to <see cref="MaxGain"/>; not a number counts as 0 dB.</summary>
    public static double Clamp(double gain) => double.IsNaN(gain) ? 0 : Math.Clamp(gain, MinGain, MaxGain);
}
