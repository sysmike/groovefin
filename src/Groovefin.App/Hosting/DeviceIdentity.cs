using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Groovefin.Core;
using Groovefin.Core.Api;
using Windows.Security.ExchangeActiveSyncProvisioning;

namespace Groovefin.App.Hosting;

/// <summary>
/// Builds the <see cref="ClientIdentity"/> sent to Jellyfin. The device ID is stable per Windows user
/// and machine, so the server sees one device across restarts and reinstalls.
/// </summary>
internal static class DeviceIdentity
{
    public static ClientIdentity Create(AppVersion version) =>
        new("Groovefin", version.Version, Environment.MachineName, DeviceId());

    private static string DeviceId()
    {
        try
        {
            var id = new EasClientDeviceInformation().Id;
            if (id != Guid.Empty)
            {
                return id.ToString("N");
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Not available here, e.g. InvalidOperationException in the unpackaged app on some systems;
            // fall back below.
        }

        var seed = Encoding.UTF8.GetBytes($"Groovefin|{Environment.MachineName}|{Environment.UserName}");
        return Convert.ToHexStringLower(SHA256.HashData(seed))[..32];
    }
}
