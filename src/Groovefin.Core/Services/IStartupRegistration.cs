namespace Groovefin.Core.Services;

/// <summary>Whether Groovefin starts with Windows (#395).</summary>
public enum StartupState
{
    Off,
    On,

    /// <summary>Turned off in Task Manager's Startup apps or in Windows Settings: only there can it be turned on.</summary>
    OffByUser,

    /// <summary>Turned off or on by the organization's policy: it cannot be changed.</summary>
    ByPolicy,

    /// <summary>This install cannot start with Windows.</summary>
    Unavailable,
}

/// <summary>
/// Starts Groovefin with Windows (#395): the package's startup task for the MSIX, the user's Run entry for the
/// unpackaged build. Such a start passes <see cref="Argument"/> (or is the startup task's activation).
/// </summary>
public interface IStartupRegistration
{
    /// <summary>The argument a start with Windows passes to the unpackaged build.</summary>
    public const string Argument = "--startup";

    Task<StartupState> GetStateAsync();

    /// <summary>Turns the start with Windows on or off; returns the state then, which may be another.</summary>
    Task<StartupState> SetAsync(bool on);
}
