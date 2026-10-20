namespace Groovefin.Audio;

/// <summary>
/// The coefficients of a peaking filter (a biquad from Robert Bristow-Johnson's "Audio EQ Cookbook"),
/// normalized so that a0 is 1: it raises or lowers the frequencies around the centre by the gain and
/// leaves the rest as it is.
/// </summary>
public readonly record struct PeakingFilter(double B0, double B1, double B2, double A1, double A2)
{
    /// <summary>Gets the filter that changes nothing.</summary>
    public static PeakingFilter Identity { get; } = new(1, 0, 0, 0, 0);

    /// <summary>Calculates the filter for <paramref name="frequency"/> in Hz at <paramref name="sampleRate"/>.</summary>
    /// <returns><see cref="Identity"/> for 0 dB, and for a centre too close to the Nyquist frequency to filter.</returns>
    public static PeakingFilter Create(double frequency, double gain, double q, int sampleRate)
    {
        if (gain == 0 || sampleRate <= 0 || frequency >= sampleRate * 0.45)
        {
            return Identity;
        }

        var a = Math.Pow(10, gain / 40);
        var w0 = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var a0 = 1 + (alpha / a);
        return new PeakingFilter(
            (1 + (alpha * a)) / a0,
            -2 * cos / a0,
            (1 - (alpha * a)) / a0,
            -2 * cos / a0,
            (1 - (alpha / a)) / a0);
    }

    /// <summary>Gets a value indicating whether this is <see cref="Identity"/>.</summary>
    public bool IsIdentity => this == Identity;
}
