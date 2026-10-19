namespace Groovefin.Core.Security;

/// <summary>Names the vault resources Groovefin uses, so all its credentials are easy to find and remove.</summary>
public static class CredentialResource
{
    public const string Prefix = "Groovefin/jellyfin/";

    /// <summary>Gets the resource name for the tokens of one server.</summary>
    public static string ForServer(string serverId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        return Prefix + serverId.Trim().ToLowerInvariant();
    }
}
