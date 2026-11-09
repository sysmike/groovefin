namespace Groovefin.Core.JumpLists;

/// <summary>One entry of the jump list: its text and what it does.</summary>
public sealed record JumpListEntry(string Title, JumpListCommand Command);

/// <summary>The jump list of the app's taskbar button and Start menu entry (#406).</summary>
public interface IJumpList
{
    /// <summary>
    /// Replaces the jump list: <paramref name="tasks"/> under Tasks, and <paramref name="playlists"/> in a group
    /// named <paramref name="playlistsGroup"/> (left out when empty). Failures are logged, not thrown: Windows may
    /// refuse a jump list, for example when the user turned jump lists off.
    /// </summary>
    Task SetAsync(IReadOnlyList<JumpListEntry> tasks, string playlistsGroup, IReadOnlyList<JumpListEntry> playlists);
}
