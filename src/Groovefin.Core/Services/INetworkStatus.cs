namespace Groovefin.Core.Services;

/// <summary>Whether the PC has a network, and whether it is metered (#327).</summary>
public interface INetworkStatus
{
    /// <summary>Gets a value indicating whether the PC has a network connection that may reach a server.</summary>
    bool IsOnline { get; }

    /// <summary>Gets a value indicating whether the connection is metered (mobile data, a capped plan).</summary>
    bool IsMetered { get; }

    /// <summary>Raised on any thread when either value may have changed.</summary>
    event EventHandler? Changed;
}
