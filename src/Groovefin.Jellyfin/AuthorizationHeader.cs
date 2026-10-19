using System.Text;
using Groovefin.Core.Api;

namespace Groovefin.Jellyfin;

/// <summary>
/// Builds the <c>Authorization: MediaBrowser ...</c> header. Unlike the SDK's own header, it always
/// carries the client, device and version, also after login, so the server can identify the session.
/// </summary>
internal static class AuthorizationHeader
{
    public const string Name = "Authorization";

    public static string Build(ClientIdentity identity, string? accessToken)
    {
        var builder = new StringBuilder("MediaBrowser ");
        Append(builder, "Client", identity.ClientName);
        builder.Append(", ");
        Append(builder, "Device", identity.DeviceName);
        builder.Append(", ");
        Append(builder, "DeviceId", identity.DeviceId);
        builder.Append(", ");
        Append(builder, "Version", identity.ClientVersion);

        if (!string.IsNullOrEmpty(accessToken))
        {
            builder.Append(", ");
            Append(builder, "Token", accessToken);
        }

        return builder.ToString();
    }

    // Jellyfin URL-decodes the values, so quotes, commas and non-ASCII device names are safe.
    private static void Append(StringBuilder builder, string key, string value) =>
        builder.Append(key).Append("=\"").Append(Uri.EscapeDataString(value)).Append('"');
}
