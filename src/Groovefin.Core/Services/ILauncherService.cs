namespace Groovefin.Core.Services;

/// <summary>Opens things outside the app.</summary>
public interface ILauncherService
{
    /// <summary>Opens the folder with the log files in File Explorer.</summary>
    Task OpenLogFolderAsync();
}
