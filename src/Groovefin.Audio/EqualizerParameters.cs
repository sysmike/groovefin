using System.Globalization;
using System.Text;

namespace Groovefin.Audio;

/// <summary>
/// What the equalizer applies: on or off, the preamp and one gain per band, all in dB. The app hands
/// these to the audio effect as one string (<see cref="Format"/>, <see cref="TryParse"/>), so a change
/// arrives whole.
/// </summary>
public sealed record EqualizerParameters
{
    /// <summary>Gets the parameters that leave the audio as it is.</summary>
    public static EqualizerParameters Off { get; } = new(false, 0, new double[EqualizerBands.Count]);

    /// <summary>Initializes a new instance. Gains are limited to the allowed range; missing bands are 0 dB.</summary>
    public EqualizerParameters(bool enabled, double preamp, IReadOnlyList<double> gains)
    {
        Enabled = enabled;
        Preamp = EqualizerBands.Clamp(preamp);
        var bands = new double[EqualizerBands.Count];
        for (var i = 0; i < bands.Length && i < gains.Count; i++)
        {
            bands[i] = EqualizerBands.Clamp(gains[i]);
        }

        Gains = bands;
    }

    public bool Enabled { get; }

    public double Preamp { get; }

    /// <summary>Gets one gain per band in <see cref="EqualizerBands.Frequencies"/>.</summary>
    public IReadOnlyList<double> Gains { get; }

    /// <summary>Gets a value indicating whether the audio changes: on, and the preamp or a band is not 0 dB.</summary>
    public bool IsActive => Enabled && (Preamp != 0 || Gains.Any(g => g != 0));

    /// <summary>Parses the text of <see cref="Format"/>.</summary>
    public static bool TryParse(string? text, out EqualizerParameters parameters)
    {
        parameters = Off;
        var parts = text?.Split(';');
        if (parts is not { Length: 3 } || parts[0] is not ("0" or "1") || !TryParseNumber(parts[1], out var preamp))
        {
            return false;
        }

        var values = parts[2].Split(',');
        var gains = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (!TryParseNumber(values[i], out gains[i]))
            {
                return false;
            }
        }

        parameters = new EqualizerParameters(parts[0] == "1", preamp, gains);
        return true;
    }

    /// <summary>Formats as <c>enabled;preamp;gain,gain,...</c> (invariant culture), e.g. <c>1;-3;6,4,0,...</c>.</summary>
    public string Format()
    {
        var text = new StringBuilder();
        text.Append(Enabled ? '1' : '0').Append(';').Append(Preamp.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        text.AppendJoin(',', Gains.Select(g => g.ToString("R", CultureInfo.InvariantCulture)));
        return text.ToString();
    }

    public bool Equals(EqualizerParameters? other) =>
        other is not null && Enabled == other.Enabled && Preamp == other.Preamp && Gains.SequenceEqual(other.Gains);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Enabled);
        hash.Add(Preamp);
        foreach (var gain in Gains)
        {
            hash.Add(gain);
        }

        return hash.ToHashCode();
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
