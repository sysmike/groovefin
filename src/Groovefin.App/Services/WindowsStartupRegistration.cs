using System.Runtime.InteropServices;
using Groovefin.App.Hosting;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Groovefin.App.Services;

/// <summary>
/// <see cref="IStartupRegistration"/> for both installs (#395): the MSIX has the startup task of its manifest
/// (<see cref="TaskId"/>), the unpackaged build a value in the user's Run key that starts it with
/// <see cref="IStartupRegistration.Argument"/>.
/// </summary>
internal sealed partial class WindowsStartupRegistration(ILogger<WindowsStartupRegistration> logger) : IStartupRegistration
{
    /// <summary>The startup task's ID in Package.appxmanifest.</summary>
    public const string TaskId = "GroovefinStartup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where Task Manager keeps a Run entry turned off: the first byte is odd then.</summary>
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ValueName = "Groovefin";

    public async Task<StartupState> GetStateAsync()
    {
        try
        {
            return AppPaths.IsPackaged ? Map((await StartupTask.GetAsync(TaskId)).State) : RunEntryState(update: true);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            LogFailed(ex, "reading");
            return StartupState.Unavailable;
        }
    }

    public async Task<StartupState> SetAsync(bool on)
    {
        try
        {
            if (AppPaths.IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                if (on)
                {
                    return Map(await task.RequestEnableAsync());
                }

                task.Disable();
                return Map(task.State);
            }

            using var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (on)
            {
                run.SetValue(ValueName, Command, RegistryValueKind.String);

                // Turned on here after Task Manager turned it off: the user's latest choice counts.
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return RunEntryState(update: false);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or ArgumentException or System.Security.SecurityException)
        {
            LogFailed(ex, on ? "turning on" : "turning off");
            return await GetStateAsync();
        }
    }

    /// <summary>The command of the Run entry: this executable, quoted, and the argument.</summary>
    private static string Command => $"\"{Environment.ProcessPath}\" {IStartupRegistration.Argument}";

    private static StartupState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => StartupState.On,
        StartupTaskState.Disabled => StartupState.Off,
        StartupTaskState.DisabledByUser => StartupState.OffByUser,
        StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => StartupState.ByPolicy,
        _ => StartupState.Unavailable,
    };

    /// <summary>
    /// The Run entry's state. With <paramref name="update"/>, an entry for another place (the ZIP was moved) is
    /// pointed at this one.
    /// </summary>
    private static StartupState RunEntryState(bool update)
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: update);
        if (run?.GetValue(ValueName) is not string command)
        {
            return StartupState.Off;
        }

        if (update && !string.Equals(command, Command, StringComparison.OrdinalIgnoreCase))
        {
            run.SetValue(ValueName, Command, RegistryValueKind.String);
        }

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1
            ? StartupState.OffByUser
            : StartupState.On;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Start with Windows: {Step} failed")]
    private partial void LogFailed(Exception exception, string step);
}
