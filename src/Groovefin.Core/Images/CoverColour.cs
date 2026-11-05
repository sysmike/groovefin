namespace Groovefin.Core.Images;

/// <summary>
/// A colour taken from a cover, for the background of the start page's album card (#367). White text
/// goes on it: <see cref="ForWhiteText"/> darkens it far enough.
/// </summary>
public readonly record struct CoverColour(byte R, byte G, byte B)
{
    /// <summary>
    /// The highest relative luminance that gives white text a contrast of 7:1, so the card's secondary text,
    /// white at about 80 %, still has 4.5:1 (WCAG AA).
    /// </summary>
    internal const double MaxLuminance = (1.05 / 7) - 0.05;

    /// <summary>Hue buckets of 15 degrees.</summary>
    private const int Buckets = 24;

    /// <summary>Below this saturation (or brightness) a pixel counts as grey, white or black.</summary>
    private const double MinSaturation = 0.3;

    private const double MinBrightness = 0.25;

    /// <summary>Below this share of colourful pixels the cover counts as grey: its average colour is used.</summary>
    private const double MinColourfulShare = 0.05;

    /// <summary>
    /// Picks the cover's most common strong colour from its pixels (4 bytes each: blue, green, red,
    /// alpha), for instance a small copy of it. A cover without much colour gets its average colour;
    /// <see langword="null"/> when there are no opaque pixels.
    /// </summary>
    public static CoverColour? Pick(ReadOnlySpan<byte> bgra)
    {
        Span<double> weights = stackalloc double[Buckets];
        Span<double> reds = stackalloc double[Buckets];
        Span<double> greens = stackalloc double[Buckets];
        Span<double> blues = stackalloc double[Buckets];
        weights.Clear();
        reds.Clear();
        greens.Clear();
        blues.Clear();
        long count = 0;
        double sumR = 0, sumG = 0, sumB = 0, colourful = 0;

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128)
            {
                continue;
            }

            // Whole channel values: the comparisons below are exact.
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            count++;
            sumR += r;
            sumG += g;
            sumB += b;

            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var brightness = max / 255.0;
            var saturation = max == 0 ? 0 : (max - min) / (double)max;
            if (saturation < MinSaturation || brightness < MinBrightness)
            {
                continue;
            }

            // Vivid, bright pixels count most.
            var weight = saturation * brightness;
            var bucket = (int)(Hue(r, g, b, max, min) / 360 * Buckets) % Buckets;
            weights[bucket] += weight;
            reds[bucket] += r * weight;
            greens[bucket] += g * weight;
            blues[bucket] += b * weight;
            colourful += weight;
        }

        if (count == 0)
        {
            return null;
        }

        if (colourful < MinColourfulShare * count)
        {
            return new CoverColour(Round(sumR / count), Round(sumG / count), Round(sumB / count));
        }

        // The strongest bucket with its two neighbours, so a colour on a bucket's edge is not split in two.
        var best = 0;
        var bestWeight = 0.0;
        for (var i = 0; i < Buckets; i++)
        {
            var weight = weights[(i + Buckets - 1) % Buckets] + weights[i] + weights[(i + 1) % Buckets];
            if (weight > bestWeight)
            {
                best = i;
                bestWeight = weight;
            }
        }

        double red = 0, green = 0, blue = 0;
        for (var offset = -1; offset <= 1; offset++)
        {
            var i = (best + offset + Buckets) % Buckets;
            red += reds[i];
            green += greens[i];
            blue += blues[i];
        }

        return new CoverColour(Round(red / bestWeight), Round(green / bestWeight), Round(blue / bestWeight));
    }

    /// <summary>Gets the relative luminance as WCAG defines it: 0 for black, 1 for white.</summary>
    public double Luminance => (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

    /// <summary>
    /// Gets this colour, darkened (keeping its hue) until white text on it has a contrast of at least 7:1.
    /// </summary>
    public CoverColour ForWhiteText()
    {
        if (Luminance <= MaxLuminance)
        {
            return this;
        }

        // The brightest scale of the channels that is dark enough; a scale of 0 is black.
        double dark = 0, light = 1;
        for (var step = 0; step < 16; step++)
        {
            var middle = (dark + light) / 2;
            if (Scaled(middle).Luminance <= MaxLuminance)
            {
                dark = middle;
            }
            else
            {
                light = middle;
            }
        }

        return Scaled(dark);
    }

    private CoverColour Scaled(double factor) => new(Round(R * factor), Round(G * factor), Round(B * factor));

    private static double Hue(int r, int g, int b, int max, int min)
    {
        double range = max - min;
        var hue = max == r ? (g - b) / range
            : max == g ? 2 + ((b - r) / range)
            : 4 + ((r - g) / range);
        return ((hue * 60) + 360) % 360;
    }

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Round(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
