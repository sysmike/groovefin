using Groovefin.App.Hosting;
using Groovefin.Core.Services;
using Windows.System;

namespace Groovefin.App.Services;

/// <summary><see cref="ILauncherService"/> on the Windows launcher.</summary>
internal sealed class LauncherService : ILauncherService
{
    public async Task OpenLogFolderAsync()
    {
        // The folder exists once something was logged; create it so a fresh install opens too.
        Directory.CreateDirectory(AppPaths.Logs);
        await Launcher.LaunchFolderPathAsync(AppPaths.Logs);
    }
}
