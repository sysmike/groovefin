using System.Runtime.InteropServices;
using Groovefin.App.Hosting;
using Groovefin.Core.JumpLists;
using Microsoft.Extensions.Logging;
using static Groovefin.App.Hosting.ShellInterop;

namespace Groovefin.App.Services;

/// <summary>
/// <see cref="IJumpList"/> for Windows (#406). The MSIX build has a package identity and uses the WinRT jump list,
/// whose entries come back as launch arguments; the unpackaged build gives the shell's jump list shortcuts to
/// <c>Groovefin.exe</c> with the arguments, under its own app ID (<see cref="AppIdentity.Id"/>).
/// </summary>
internal sealed partial class WindowsJumpList(ILogger<WindowsJumpList> logger) : IJumpList
{
    /// <summary><c>PKEY_Title</c>: a shortcut's text in the jump list.</summary>
    private static readonly PropertyKey TitleKey = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);

    public async Task SetAsync(IReadOnlyList<JumpListEntry> tasks, string playlistsGroup, IReadOnlyList<JumpListEntry> playlists)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(playlists);
        try
        {
            if (AppPaths.IsPackaged)
            {
                await SetPackagedAsync(tasks, playlistsGroup, playlists);
            }
            else
            {
                SetUnpackaged(tasks, playlistsGroup, playlists);
            }

            LogUpdated(tasks.Count, playlists.Count);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidCastException or InvalidOperationException)
        {
            // The taskbar and Start menu then keep the previous jump list, or none.
            LogFailed(ex);
        }
    }

    private static async Task SetPackagedAsync(IReadOnlyList<JumpListEntry> tasks, string playlistsGroup, IReadOnlyList<JumpListEntry> playlists)
    {
        if (!Windows.UI.StartScreen.JumpList.IsSupported())
        {
            throw new InvalidOperationException("This Windows has no jump lists for the app.");
        }

        var list = await Windows.UI.StartScreen.JumpList.LoadCurrentAsync();
        list.SystemGroupKind = Windows.UI.StartScreen.JumpListSystemGroupKind.None;
        list.Items.Clear();
        foreach (var task in tasks)
        {
            list.Items.Add(Windows.UI.StartScreen.JumpListItem.CreateWithArguments(task.Command.Arguments, task.Title));
        }

        foreach (var playlist in playlists)
        {
            var item = Windows.UI.StartScreen.JumpListItem.CreateWithArguments(playlist.Command.Arguments, playlist.Title);
            item.GroupName = playlistsGroup;
            list.Items.Add(item);
        }

        await list.SaveAsync();
    }

    private void SetUnpackaged(IReadOnlyList<JumpListEntry> tasks, string playlistsGroup, IReadOnlyList<JumpListEntry> playlists)
    {
        var program = Environment.ProcessPath ?? throw new InvalidOperationException("The executable's path is unknown.");
        var list = (ICustomDestinationList)new DestinationList();
        try
        {
            list.SetAppID(AppIdentity.Id);
            var arrayId = typeof(IObjectArray).GUID;
            list.BeginList(out _, ref arrayId, out var removed);
            Release(removed);

            if (playlists.Count > 0)
            {
                var group = Collect(program, playlists);
                try
                {
                    var result = list.AppendCategory(playlistsGroup, (IObjectArray)group);
                    if (result < 0)
                    {
                        // E_ACCESSDENIED when recent items in jump lists are off: the tasks still show.
                        LogPlaylistsRefused(result);
                    }
                }
                finally
                {
                    Release(group);
                }
            }

            var taskItems = Collect(program, tasks);
            try
            {
                list.AddUserTasks((IObjectArray)taskItems);
            }
            finally
            {
                Release(taskItems);
            }

            list.CommitList();
        }
        catch (COMException)
        {
            try
            {
                list.AbortList();
            }
            catch (COMException)
            {
                // Nothing was begun.
            }

            throw;
        }
        finally
        {
            Release(list);
        }
    }

    /// <summary>A shortcut to <paramref name="program"/> with each entry's arguments and title.</summary>
    private static IObjectCollection Collect(string program, IReadOnlyList<JumpListEntry> entries)
    {
        var collection = (IObjectCollection)new EnumerableObjectCollection();
        foreach (var entry in entries)
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(program);
            link.SetArguments(entry.Command.Arguments);
            link.SetIconLocation(program, 0);
            link.SetDescription(entry.Title);

            var store = (IPropertyStore)link;
            var key = TitleKey;
            var title = PropVariant.FromString(entry.Title);
            try
            {
                store.SetValue(ref key, ref title);
                store.Commit();
            }
            finally
            {
                PropVariantClear(ref title);
            }

            collection.AddObject(link);
            Release(link);
        }

        return collection;
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Jump list updated with {Tasks} tasks and {Playlists} playlists")]
    private partial void LogUpdated(int tasks, int playlists);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jump list: Windows did not take the playlists (0x{Result:X8}); recent items in jump lists may be off")]
    private partial void LogPlaylistsRefused(int result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The jump list could not be updated")]
    private partial void LogFailed(Exception exception);
}
