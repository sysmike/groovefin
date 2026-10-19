using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Groovefin.Core.Servers;

/// <summary>Tells local servers (home network) from remote ones by their address.</summary>
public static class NetworkLocation
{
    private static readonly string[] LocalSuffixes = [".local", ".lan", ".home", ".internal", ".home.arpa", ".localdomain"];

    /// <summary>Private (RFC 1918) and link-local IPv4 networks.</summary>
    private static readonly (uint Network, uint Mask)[] PrivateIPv4 =
    [
        Range(10, 0, 0, 0, 8),
        Range(172, 16, 0, 0, 12),
        Range(192, 168, 0, 0, 16),
        Range(169, 254, 0, 0, 16),
    ];

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

        var value = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
        return PrivateIPv4.Any(range => (value & range.Mask) == range.Network);
    }

    /// <summary>An IPv4 network in CIDR form, e.g. <c>Range(172, 16, 0, 0, 12)</c> for 172.16.0.0/12.</summary>
    private static (uint Network, uint Mask) Range(byte a, byte b, byte c, byte d, int prefixLength)
    {
        var mask = uint.MaxValue << (32 - prefixLength);
        return ((((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d) & mask, mask);
    }
}
