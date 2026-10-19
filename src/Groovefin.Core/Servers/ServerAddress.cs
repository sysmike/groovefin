namespace Groovefin.Core.Servers;

/// <summary>Normalizes what the user typed into a server address.</summary>
public static class ServerAddress
{
    /// <summary>
    /// Accepts <c>host</c>, <c>host:port</c>, <c>http(s)://host[:port][/path]</c>. A missing scheme
    /// means <c>http</c> (Jellyfin's default on a local network). Query and fragment are dropped, as is
    /// a trailing slash.
    /// </summary>
    public static bool TryNormalize(string? input, out Uri address)
    {
        address = null!;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        builder.Path = builder.Path.TrimEnd('/');
        address = builder.Uri;
        return true;
    }
}
