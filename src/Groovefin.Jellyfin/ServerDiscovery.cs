using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Groovefin.Core.Servers;
using Microsoft.Extensions.Logging;

namespace Groovefin.Jellyfin;

/// <summary>
/// Jellyfin's own discovery (#405): a UDP datagram "who is JellyfinServer?" to port 7359, which every Jellyfin
/// server answers with a little JSON (<c>Address</c>, <c>Id</c>, <c>Name</c>). It goes to the broadcast address
/// of each network the PC is on (Windows sends 255.255.255.255 out of one network only), to
/// 255.255.255.255, and to this PC itself for a server running here.
/// </summary>
public sealed partial class ServerDiscovery : IServerDiscovery
{
    /// <summary>The port Jellyfin servers listen on for discovery.</summary>
    public const int Port = 7359;

    /// <summary>The question every Jellyfin server answers.</summary>
    internal const string Question = "who is JellyfinServer?";

    /// <summary>How long answers are waited for.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<IReadOnlyList<IPEndPoint>> _targets;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;

    public ServerDiscovery(ILogger<ServerDiscovery> logger)
        : this(LocalTargets, DefaultTimeout, logger)
    {
    }

    /// <summary>Asks <paramref name="targets"/> instead of the local network (tests answer on the loopback address).</summary>
    internal ServerDiscovery(Func<IReadOnlyList<IPEndPoint>> targets, TimeSpan timeout, ILogger logger)
    {
        _targets = targets;
        _timeout = timeout;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, DiscoveredServer>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            var question = Encoding.UTF8.GetBytes(Question);
            foreach (var target in _targets())
            {
                try
                {
                    await udp.SendAsync(question, target, cancellationToken).ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    // A network without a route, for example: the others can still answer.
                    LogSendFailed(target, ex.SocketErrorCode);
                }
            }

            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waiting.CancelAfter(_timeout);
            while (true)
            {
                UdpReceiveResult answer;
                try
                {
                    answer = await udp.ReceiveAsync(waiting.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // Windows reports an earlier datagram that nobody received (no server on this PC) here.
                    continue;
                }

                if (Parse(answer.Buffer) is { } server)
                {
                    found.TryAdd(server.Id, server);
                }
            }
        }
        catch (SocketException ex)
        {
            LogDiscoveryFailed(ex.SocketErrorCode);
        }

        LogFound(found.Count);
        return [.. found.Values];
    }

    /// <summary>
    /// Reads a server's answer, or <see langword="null"/> when it is not one: not JSON, without an ID, or with an
    /// address that is not http(s).
    /// </summary>
    internal static DiscoveredServer? Parse(ReadOnlySpan<byte> answer)
    {
        try
        {
            using var json = JsonDocument.Parse(answer.ToArray());
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = Text(root, "Id")?.Replace("-", string.Empty, StringComparison.Ordinal);
            var address = Text(root, "Address");
            if (string.IsNullOrWhiteSpace(id)
                || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            var name = Text(root, "Name");
            return new DiscoveredServer(id, string.IsNullOrWhiteSpace(name) ? uri.Host : name, uri);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Text(JsonElement root, string property) =>
            root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>The broadcast address of the network <paramref name="address"/> is on with <paramref name="mask"/>.</summary>
    internal static IPAddress BroadcastAddress(IPAddress address, IPAddress mask)
    {
        var bytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] |= (byte)~maskBytes[i];
        }

        return new IPAddress(bytes);
    }

    /// <summary>Each network's broadcast address, 255.255.255.255 and this PC.</summary>
    private static IReadOnlyList<IPEndPoint> LocalTargets()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask is { } mask && !mask.Equals(IPAddress.Any))
                    {
                        addresses.Add(BroadcastAddress(unicast.Address, mask));
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Then the two fixed addresses only.
        }

        return [.. addresses.Select(a => new IPEndPoint(a, Port))];
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server discovery could not ask {Target}: {Error}")]
    private partial void LogSendFailed(IPEndPoint target, SocketError error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server discovery failed: {Error}")]
    private partial void LogDiscoveryFailed(SocketError error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server discovery found {Count} servers")]
    private partial void LogFound(int count);
}
