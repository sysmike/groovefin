using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Groovefin.App;

/// <summary>
/// The entry point, instead of the one XAML generates (<c>DISABLE_XAML_GENERATED_MAIN</c>): Groovefin runs once
/// (#406). A second start, from the Start menu or a jump list entry, hands its arguments to the running app,
/// which comes to the front and runs the entry's command, and ends.
/// </summary>
internal static partial class Program
{
    private const string InstanceKey = "Groovefin";

    private static readonly Lock Gate = new();
    private static readonly List<AppActivationArguments> Pending = [];
    private static Action<AppActivationArguments>? _handler;

    [STAThread]
    private static int Main()
    {
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (RedirectToRunningApp())
        {
            return 0;
        }

        Application.Start(callback =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    /// <summary>
    /// Sets what happens when another start arrives: <paramref name="handler"/> runs on a thread-pool thread, first
    /// for each start that arrived before it was set.
    /// </summary>
    public static void OnRedirected(Action<AppActivationArguments> handler)
    {
        AppActivationArguments[] waiting;
        lock (Gate)
        {
            _handler = handler;
            waiting = [.. Pending];
            Pending.Clear();
        }

        foreach (var args in waiting)
        {
            handler(args);
        }
    }

    /// <summary>
    /// Stops taking other starts, when the window closes: a start while this app shuts down opens a new one
    /// instead of reaching a closing app.
    /// </summary>
    public static void StopTakingStarts()
    {
        try
        {
            AppInstance.GetCurrent().UnregisterKey();
        }
        catch (COMException)
        {
            // Then the key goes with the process.
        }
    }

    /// <summary>The arguments of a start, or <see langword="null"/> when it was not a plain launch.</summary>
    public static string? ArgumentsOf(AppActivationArguments args) =>
        args.Kind == ExtendedActivationKind.Launch && args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
            ? launch.Arguments
            : null;

    /// <summary>
    /// Registers this process as Groovefin, or hands this start to the one already registered. Returns
    /// <see langword="true"/> when the start went to it. When the registration fails, this process runs on its own.
    /// </summary>
    private static bool RedirectToRunningApp()
    {
        AppInstance running;
        try
        {
            running = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (running.IsCurrent)
            {
                running.Activated += OnActivated;
                return false;
            }
        }
        catch (COMException)
        {
            return false;
        }

        // This start came from the user, so it may give the running app the right to come to the front.
        _ = AllowSetForegroundWindow((int)running.ProcessId);

        // The redirection runs on another thread. A wait on this STA thread pumps COM messages meanwhile, so the
        // redirection's calls back into this process are not blocked.
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        using var done = new ManualResetEvent(initialState: false);
        _ = Task.Run(() =>
        {
            try
            {
                running.RedirectActivationToAsync(args).AsTask().Wait(TimeSpan.FromSeconds(10));
            }
            catch (AggregateException)
            {
                // The running app may be closing; this start ends anyway.
            }
            finally
            {
                done.Set();
            }
        });
        done.WaitOne();
        return true;
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        Action<AppActivationArguments>? handler;
        lock (Gate)
        {
            handler = _handler;
            if (handler is null)
            {
                Pending.Add(args);
                return;
            }
        }

        handler(args);
    }

    [DllImport("Microsoft.ui.xaml.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void XamlCheckProcessRequirements();

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
