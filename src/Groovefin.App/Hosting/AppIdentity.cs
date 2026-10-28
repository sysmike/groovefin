using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

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

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    /// <summary>A <c>PROPVARIANT</c> holding at most a string (<c>VT_LPWSTR</c>); 24 bytes on 64-bit Windows.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        private const ushort VtLpwstr = 31;

        [FieldOffset(0)]
        private ushort _type;

        [FieldOffset(8)]
        private IntPtr _pointer;

        /// <summary>Copies <paramref name="value"/> into task memory; <see cref="PropVariantClear"/> frees it.</summary>
        public static PropVariant FromString(string value) =>
            new() { _type = VtLpwstr, _pointer = Marshal.StringToCoTaskMemUni(value) };

        public readonly string? ToStringOrNull() => _type == VtLpwstr ? Marshal.PtrToStringUni(_pointer) : null;
    }

    /// <summary>The shell's shortcut object (<c>CLSID_ShellLink</c>).</summary>
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    private class ShellLink;

    /// <summary><c>IShellLinkW</c> up to the methods used; the order must match the vtable.</summary>
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] file, int size, IntPtr findData, uint flags);

        void GetIDList(out IntPtr idList);

        void SetIDList(IntPtr idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] name, int size);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] directory, int size);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] arguments, int size);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out ushort hotkey);

        void SetHotkey(ushort hotkey);

        void GetShowCmd(out int showCommand);

        void SetShowCmd(int showCommand);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] iconPath, int size, out int index);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

        void Resolve(IntPtr window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    /// <summary><c>IPropertyStore</c>; the order must match the vtable.</summary>
    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);

        void GetAt(uint index, out PropertyKey key);

        void GetValue(ref PropertyKey key, out PropVariant value);

        void SetValue(ref PropertyKey key, ref PropVariant value);

        void Commit();
    }
}
