namespace Groovefin.Core.Models;

/// <summary>Public information about a server, available without logging in.</summary>
public sealed record ServerInfo(string Id, string Name, string Version, Uri Address);
