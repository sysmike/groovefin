namespace Groovefin.Core.Api;

/// <summary>
/// How Groovefin identifies itself to the server. Jellyfin shows these values in its dashboard and
/// uses the device ID to tell sessions apart, so <see cref="DeviceId"/> must be stable per install.
/// </summary>
public sealed record ClientIdentity(string ClientName, string ClientVersion, string DeviceName, string DeviceId);
