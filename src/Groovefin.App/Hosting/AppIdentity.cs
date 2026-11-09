using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using static Groovefin.App.Hosting.ShellInterop;

namespace Groovefin.App.Hosting;

/// <summary>
/// Gives the unpackaged app (the ZIP) an identity Windows can name (#271). Without a package, the process
/// has only a path-derived AppUserModelID, which the shell cannot resolve to a name, so the media flyout
/// says "Unknown application". The process therefore sets <see cref="Id"/> itself, and a per-user Start
/// menu shortcut carries the same ID: that shortcut is where the shell finds the name and the icon.
/// The MSIX build has its package identity and needs neither.
/// </summary>
internal static class AppIdentity
{
    /// <summary>The unpackaged app's AppUserModelID.</summary>
    public const string Id = "Groovefin";

    private const string Name = "Groovefin";

    /// <summary><c>PKEY_AppUserModel_ID</c>.</summary>
    private static readonly PropertyKey AppUserModelIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    /// <summary>Gets the Start menu shortcut's path: <c>%APPDATA%\Microsoft\Windows\Start Menu\Programs\Groovefin.lnk</c>.</summary>
    public static string ShortcutPath { get; } =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk");

    /// <summary>
    /// Sets the process's AppUserModelID. Must run before the app shows a window or creates its media player.
    /// </summary>
    /// <exception cref="COMException">Windows refused the ID.</exception>
    public static void SetProcessId() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(Id));

    /// <summary>
    /// Creates the Start menu shortcut, or points it at this executable when it targets another copy
    /// (the latest copy started wins). Leaves an up-to-date shortcut alone.
    /// </summary>
    /// <returns><see langword="true"/> when the shortcut was written, <see langword="false"/> when it was up to date.</returns>
    /// <exception cref="COMException">The shell could not read or write the shortcut.</exception>
    /// <exception cref="UnauthorizedAccessException">The Start menu folder cannot be written.</exception>
    public static bool EnsureShortcut()
    {
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("The executable's path is unknown.");

        if (File.Exists(ShortcutPath) && IsUpToDate(target))
        {
            return false;
        }

        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetIconLocation(target, 0);
            link.SetDescription(Name);

            var store = (IPropertyStore)link;
            var key = AppUserModelIdKey;
            var value = PropVariant.FromString(Id);
            try
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally
            {
                PropVariantClear(ref value);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
            ((IPersistFile)link).Save(ShortcutPath, true);
            return true;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    private static bool IsUpToDate(string target)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            // STGM_READ.
            ((IPersistFile)link).Load(ShortcutPath, 0);

            var path = new char[1024];
            link.GetPath(path, path.Length, IntPtr.Zero, 0);
            if (!string.Equals(new string(path).TrimEnd('\0'), target, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var key = AppUserModelIdKey;
            ((IPropertyStore)link).GetValue(ref key, out var value);
            try
            {
                return value.ToStringOrNull() == Id;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            // Unreadable or broken: write a new one.
            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
