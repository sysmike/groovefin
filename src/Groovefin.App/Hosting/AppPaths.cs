using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Storage;

namespace Groovefin.App.Hosting;

/// <summary>
/// Resolves local storage locations for packaged (MSIX) and unpackaged (ZIP) installs.
/// </summary>
internal static class AppPaths
{
    private static readonly Lazy<bool> PackagedState = new(DetectPackaged);

    /// <summary>Gets a value indicating whether the app runs with package identity.</summary>
    public static bool IsPackaged => PackagedState.Value;

    /// <summary>
    /// Gets the root folder for settings, caches and logs:
    /// <c>ApplicationData.LocalFolder</c> when packaged, <c>%LOCALAPPDATA%\Groovefin</c> otherwise.
    /// </summary>
    public static string LocalData { get; } = ResolveLocalData();

    public static string Logs => Path.Combine(LocalData, "Logs");

    private static string ResolveLocalData()
    {
        var path = IsPackaged
            ? ApplicationData.Current.LocalFolder.Path
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Groovefin");
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool DetectPackaged()
    {
        try
        {
            return Package.Current is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // No package identity (the unpackaged ZIP): APPMODEL_ERROR_NO_PACKAGE arrives as a COMException.
            return false;
        }
    }
}
