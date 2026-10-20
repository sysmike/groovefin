using System.Globalization;
using Groovefin.Core.Services;

namespace Groovefin.Core.Formatting;

/// <summary>Formats storage sizes for display.</summary>
public static class ByteSizes
{
    private const long Megabyte = 1024L * 1024L;
    private const long Gigabyte = 1024L * Megabyte;

    /// <summary>
    /// A size in megabytes or, from one gigabyte, in gigabytes: "0.4 MB", "250 MB", "1.5 GB"
    /// (localized via resources; one decimal below 10).
    /// </summary>
    public static string Format(long bytes, IStringResources strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        var (value, key) = bytes >= Gigabyte
            ? ((double)bytes / Gigabyte, "Size_Gigabytes")
            : ((double)bytes / Megabyte, "Size_Megabytes");
        var number = value.ToString(value < 10 ? "0.#" : "N0", CultureInfo.CurrentCulture);
        return strings.GetString(key).Replace("{0}", number, StringComparison.Ordinal);
    }
}
