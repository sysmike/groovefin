using System.Net;
using System.Net.Sockets;

namespace Groovefin.Core.Servers;

/// <summary>Tells local servers (home network) from remote ones by their address.</summary>
public static class NetworkLocation
{
    private static readonly string[] LocalSuffixes = [".local", ".lan", ".home", ".internal", ".home.arpa", ".localdomain"];

    /// <summary>
    /// Gets a value indicating whether <paramref name="serverUrl"/> points into the local network: loopback,
    /// private and link-local IPs (IPv4 and IPv6), single-label host names and local DNS suffixes such
    /// as <c>.local</c> or <c>.home.arpa</c>. Anything else counts as remote, also carrier-grade NAT
    /// addresses (100.64.0.0/10) that VPNs such as Tailscale use: those are often reached over mobile data.
    /// </summary>
    public static bool IsLocal(Uri serverUrl)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);

        if (serverUrl.IsLoopback)
        {
            return true;
        }

        var host = serverUrl.IdnHost;
        if (IPAddress.TryParse(host, out var address))
        {
            return IsLocal(address);
        }

        return !host.Contains('.', StringComparison.Ordinal)
            || LocalSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Link-local fe80::/10 and unique local fc00::/7.
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal || address.IsIPv6SiteLocal;
        }

        var b = address.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }
}
