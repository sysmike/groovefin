namespace Groovefin.Core.Servers;

/// <summary>Normalizes what the user typed into a server address.</summary>
public static class ServerAddress
{
    /// <summary>Jellyfin's default HTTPS port.</summary>
    public const int JellyfinHttpsPort = 8920;

    /// <summary>
    /// Accepts <c>host</c>, <c>host:port</c>, <c>http(s)://host[:port][/path]</c>. A missing scheme
    /// means <c>http</c> (Jellyfin's default on a local network), except with an HTTPS port: 443 or
    /// Jellyfin's 8920 (#293). Query and fragment are dropped, as is a trailing slash.
    /// </summary>
    public static bool TryNormalize(string? input, out Uri address)
    {
        address = null!;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var withoutScheme = !text.Contains("://", StringComparison.Ordinal);
        if (withoutScheme)
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
        if (withoutScheme && uri.Port is 443 or JellyfinHttpsPort)
        {
            builder.Scheme = Uri.UriSchemeHttps;
        }

        builder.Path = builder.Path.TrimEnd('/');
        address = builder.Uri;
        return true;
    }

    /// <summary>
    /// Gets a value indicating whether the address is plain HTTP (#293): the password at sign-in and the
    /// access token with every request then cross the network unencrypted, also on a home network.
    /// </summary>
    public static bool IsUnencrypted(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Scheme == Uri.UriSchemeHttp;
    }
}
