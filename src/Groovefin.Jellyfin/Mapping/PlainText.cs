using System.Net;
using System.Text.RegularExpressions;

namespace Groovefin.Jellyfin.Mapping;

/// <summary>
/// Turns the HTML fragments some metadata providers put into overviews into plain text for display in a
/// TextBlock. The result is never rendered as HTML.
/// </summary>
internal static partial class PlainText
{
    public static string? FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = LineBreak().Replace(html, "\n");
        text = Tag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = BlankLines().Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal), "\n\n").Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex(@"<\s*br\s*/?\s*>|<\s*/\s*p\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Tag();

    [GeneratedRegex(@"\n[ \t]*\n(\s*\n)*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BlankLines();
}
