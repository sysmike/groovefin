using System.Runtime.InteropServices;

namespace Groovefin.App.Hosting;

/// <summary>
/// The shell's COM interfaces for shortcuts (the unpackaged app's identity, #271) and the jump list (#406), and
/// the <c>PROPVARIANT</c> they take. Only the members used are declared, in vtable order.
/// </summary>
internal static class ShellInterop
{
    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    /// <summary>A <c>PROPVARIANT</c> holding at most a string (<c>VT_LPWSTR</c>); 24 bytes on 64-bit Windows.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
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
    internal class ShellLink;

    /// <summary><c>IShellLinkW</c> up to the methods used; the order must match the vtable.</summary>
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
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
    internal interface IPropertyStore
    {
        void GetCount(out uint count);

        void GetAt(uint index, out PropertyKey key);

        void GetValue(ref PropertyKey key, out PropVariant value);

        void SetValue(ref PropertyKey key, ref PropVariant value);

        void Commit();
    }

    /// <summary>The shell's jump list (<c>CLSID_DestinationList</c>), for the unpackaged app (#406).</summary>
    [ComImport]
    [Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6")]
    [ClassInterface(ClassInterfaceType.None)]
    internal class DestinationList;

    /// <summary>A list of shell objects to hand to the jump list (<c>CLSID_EnumerableObjectCollection</c>).</summary>
    [ComImport]
    [Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A")]
    [ClassInterface(ClassInterfaceType.None)]
    internal class EnumerableObjectCollection;

    /// <summary><c>ICustomDestinationList</c>; the order must match the vtable.</summary>
    [ComImport]
    [Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        void BeginList(out uint minSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);

        /// <summary>Fails with <c>E_ACCESSDENIED</c> when the user turned off recent items in jump lists.</summary>
        [PreserveSig]
        int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);

        void AppendKnownCategory(int category);

        void AddUserTasks(IObjectArray tasks);

        void CommitList();

        void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);

        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string? appId);

        void AbortList();
    }

    /// <summary><c>IObjectArray</c>; the order must match the vtable.</summary>
    [ComImport]
    [Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IObjectArray
    {
        void GetCount(out uint count);

        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    /// <summary><c>IObjectCollection</c>: <see cref="IObjectArray"/>'s methods first, as in its vtable.</summary>
    [ComImport]
    [Guid("5632B1A4-E38A-400A-928A-D4CD63230295")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IObjectCollection
    {
        void GetCount(out uint count);

        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

        void AddObject([MarshalAs(UnmanagedType.Interface)] object item);

        void AddFromArray(IObjectArray items);

        void RemoveObjectAt(uint index);

        void Clear();
    }
}
