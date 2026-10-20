namespace Groovefin.Audio;

/// <summary>The ten bands of the graphic equalizer: one octave each, at the ISO centre frequencies.</summary>
public static class EqualizerBands
{
    /// <summary>The lowest gain of a band and of the preamp, in dB.</summary>
    public const double MinGain = -12;

    /// <summary>The highest gain of a band and of the preamp, in dB.</summary>
    public const double MaxGain = 12;

    /// <summary>
    /// Gains closer than this (in dB) count as the same: far below what anyone can hear, and the sliders move
    /// in 0.5 dB steps. <see cref="Clamp"/> rounds to 0.01 dB, so values that differ always differ by more.
    /// </summary>
    public const double Tolerance = 0.001;

    /// <summary>The quality factor of each band's filter: one octave wide.</summary>
    public static readonly double Q = Math.Sqrt(2);

    /// <summary>Gets the centre frequencies in Hz, lowest first.</summary>
    public static IReadOnlyList<double> Frequencies { get; } = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>Gets the number of bands.</summary>
    public static int Count => Frequencies.Count;

    /// <summary>Limits a gain to <see cref="MinGain"/> to <see cref="MaxGain"/> in steps of 0.01 dB; not a number counts as 0 dB.</summary>
    public static double Clamp(double gain) =>
        double.IsNaN(gain) ? 0 : Math.Round(Math.Clamp(gain, MinGain, MaxGain), 2) + 0.0; // + 0.0 turns -0 into 0.

    /// <summary>Gets a value indicating whether a gain is 0 dB (within <see cref="Tolerance"/>).</summary>
    public static bool IsFlat(double gain) => Math.Abs(gain) < Tolerance;

    /// <summary>Gets a value indicating whether two gains are the same (within <see cref="Tolerance"/>).</summary>
    public static bool AreEqual(double a, double b) => Math.Abs(a - b) < Tolerance;
}
