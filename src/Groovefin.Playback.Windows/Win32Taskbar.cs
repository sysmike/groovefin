using System.Runtime.InteropServices;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Groovefin.Playback.Windows;

/// <summary>
/// <see cref="ITaskbar"/> on the shell's <c>ITaskbarList3</c> for one window: three thumbnail toolbar
/// buttons and the progress on the taskbar button. Button clicks arrive as <c>WM_COMMAND</c>, so the
/// window procedure is subclassed. The icons match the taskbar's light or dark theme. If the shell
/// refuses (no taskbar, Explorer not running), the failure is logged and the taskbar stays plain.
/// </summary>
public sealed partial class Win32Taskbar : ITaskbar, IDisposable
{
    private const uint WmCommand = 0x0111;
    private const int ThbnClicked = 0x1800;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x10;
    private const int SmallIconWidth = 49;
    private static readonly UIntPtr SubclassId = new(0x4746);

    private readonly IntPtr _window;
    private readonly IStringResources _strings;
    private readonly ILogger<Win32Taskbar> _logger;
    private readonly SubclassProc _subclass;
    private readonly uint _taskbarCreated;
    private readonly IntPtr[] _icons;
    private readonly ITaskbarList3? _list;
    private bool _buttonsAdded;
    private bool _hasTrack;
    private bool _isPlaying;

    /// <summary>Adds the thumbnail buttons and progress to <paramref name="window"/>.</summary>
    /// <param name="window">The main window's handle.</param>
    /// <param name="iconFolder">The folder with <c>previous|play|pause|next-light|dark.ico</c>.</param>
    public Win32Taskbar(IntPtr window, string iconFolder, IStringResources strings, ILogger<Win32Taskbar> logger)
    {
        _window = window;
        _strings = strings;
        _logger = logger;
        _subclass = OnWindowMessage;
        _taskbarCreated = RegisterWindowMessage("TaskbarButtonCreated");
        _icons = LoadIcons(iconFolder);

        try
        {
            _list = (ITaskbarList3)new TaskbarList();
            _list.HrInit();
            SetWindowSubclass(window, _subclass, SubclassId, UIntPtr.Zero);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            LogUnavailable(ex);
            _list = null;
        }
    }

    public event EventHandler<TaskbarButton>? ButtonPressed;

    private enum Icon
    {
        Previous,
        Play,
        Pause,
        Next,
    }

    public void UpdateButtons(bool hasTrack, bool isPlaying)
    {
        _hasTrack = hasTrack;
        _isPlaying = isPlaying;
        if (_list is null)
        {
            return;
        }

        try
        {
            if (_buttonsAdded)
            {
                _list.ThumbBarUpdateButtons(_window, 3, Buttons());
            }
            else
            {
                AddButtons();
            }
        }
        catch (COMException ex)
        {
            LogCallFailed(ex);
        }
    }

    public void SetProgress(TaskbarProgressState state, double fraction)
    {
        if (_list is null)
        {
            return;
        }

        try
        {
            _list.SetProgressState(_window, state switch
            {
                TaskbarProgressState.Normal => ProgressFlags.Normal,
                TaskbarProgressState.Paused => ProgressFlags.Paused,
                TaskbarProgressState.Indeterminate => ProgressFlags.Indeterminate,
                _ => ProgressFlags.NoProgress,
            });
            if (state is TaskbarProgressState.Normal or TaskbarProgressState.Paused)
            {
                _list.SetProgressValue(_window, (ulong)Math.Round(Math.Clamp(fraction, 0, 1) * 1000), 1000);
            }
        }
        catch (COMException ex)
        {
            LogCallFailed(ex);
        }
    }

    public void Dispose()
    {
        RemoveWindowSubclass(_window, _subclass, SubclassId);
        foreach (var icon in _icons.Where(i => i != IntPtr.Zero))
        {
            DestroyIcon(icon);
        }
    }

    /// <summary>The buttons can be added once the taskbar button exists; until then, and after Explorer restarts, the shell says so.</summary>
    private void AddButtons()
    {
        try
        {
            _list!.ThumbBarAddButtons(_window, 3, Buttons());
            _buttonsAdded = true;
        }
        catch (COMException)
        {
            // Not there yet: "TaskbarButtonCreated" follows.
        }
    }

    private ThumbButton[] Buttons()
    {
        var flags = _hasTrack ? ThumbButtonFlags.Enabled : ThumbButtonFlags.Disabled;
        return
        [
            Button(TaskbarButton.Previous, Icon.Previous, "Taskbar_Previous", flags),
            Button(TaskbarButton.PlayPause, _isPlaying ? Icon.Pause : Icon.Play, _isPlaying ? "Taskbar_Pause" : "Taskbar_Play", flags),
            Button(TaskbarButton.Next, Icon.Next, "Taskbar_Next", flags),
        ];
    }

    private ThumbButton Button(TaskbarButton id, Icon icon, string tipKey, ThumbButtonFlags flags) => new()
    {
        Mask = ThumbButtonMask.Icon | ThumbButtonMask.Tooltip | ThumbButtonMask.Flags,
        Id = (uint)id,
        IconHandle = _icons[(int)icon],
        Tip = _strings.GetString(tipKey),
        Flags = flags,
    };

    private IntPtr OnWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == _taskbarCreated)
        {
            // New taskbar button (first show or Explorer restarted): add the buttons again.
            _buttonsAdded = false;
            UpdateButtons(_hasTrack, _isPlaying);
        }
        else if (message == WmCommand && (((long)wParam >> 16) & 0xFFFF) == ThbnClicked)
        {
            var button = (int)((long)wParam & 0xFFFF);
            if (Enum.IsDefined((TaskbarButton)button))
            {
                ButtonPressed?.Invoke(this, (TaskbarButton)button);
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    /// <summary>Light taskbars get dark glyphs and vice versa.</summary>
    private IntPtr[] LoadIcons(string folder)
    {
        var theme = UsesLightTaskbar() ? "light" : "dark";
        var size = GetSystemMetrics(SmallIconWidth);
        return
        [
            .. Enum.GetValues<Icon>().Select(icon =>
            {
                var path = Path.Join(folder, $"{icon.ToString().ToLowerInvariant()}-{theme}.ico");
                var handle = LoadImage(IntPtr.Zero, path, ImageIcon, size, size, LoadFromFile);
                if (handle == IntPtr.Zero)
                {
                    LogIconMissing(path);
                }

                return handle;
            }),
        ];
    }

    private static bool UsesLightTaskbar() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The taskbar is not available; no thumbnail buttons or progress")]
    private partial void LogUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A taskbar call failed")]
    private partial void LogCallFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Taskbar icon {Path} could not be loaded")]
    private partial void LogIconMissing(string path);

    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProc proc, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc proc, UIntPtr id);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetSystemMetrics(int index);

    [Flags]
    private enum ThumbButtonMask : uint
    {
        Icon = 0x2,
        Tooltip = 0x4,
        Flags = 0x8,
    }

    [Flags]
    private enum ThumbButtonFlags : uint
    {
        Enabled = 0x0,
        Disabled = 0x1,
    }

    private enum ProgressFlags : uint
    {
        NoProgress = 0x0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Paused = 0x8,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ThumbButton
    {
        public ThumbButtonMask Mask;
        public uint Id;
        public uint Bitmap;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Tip;

        public ThumbButtonFlags Flags;
    }

    /// <summary>The shell's taskbar list (<c>CLSID_TaskbarList</c>).</summary>
    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList;

    /// <summary><c>ITaskbarList3</c> up to the methods used; the order must match the vtable.</summary>
    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();

        void AddTab(IntPtr window);

        void DeleteTab(IntPtr window);

        void ActivateTab(IntPtr window);

        void SetActiveAlt(IntPtr window);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr window, ulong completed, ulong total);

        void SetProgressState(IntPtr window, ProgressFlags flags);

        void RegisterTab(IntPtr tab, IntPtr mdi);

        void UnregisterTab(IntPtr tab);

        void SetTabOrder(IntPtr tab, IntPtr insertBefore);

        void SetTabActive(IntPtr tab, IntPtr mdi, uint reserved);

        void ThumbBarAddButtons(IntPtr window, uint count, [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] buttons);

        void ThumbBarUpdateButtons(IntPtr window, uint count, [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] buttons);
    }
}
