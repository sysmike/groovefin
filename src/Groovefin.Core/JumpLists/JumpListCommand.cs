using System.Text.RegularExpressions;

namespace Groovefin.Core.JumpLists;

/// <summary>What a jump list entry does (#406).</summary>
public enum JumpListAction
{
    /// <summary>Plays or pauses the queue.</summary>
    PlayPause,

    /// <summary>Plays the next track.</summary>
    Next,

    /// <summary>Plays songs from the whole library in a random order.</summary>
    ShuffleAll,

    /// <summary>Plays a playlist, <see cref="JumpListCommand.PlaylistId"/>.</summary>
    PlayPlaylist,
}

/// <summary>
/// A jump list entry's command (#406). Windows starts Groovefin with <see cref="Arguments"/>; a start while the app
/// runs is handed to the running app, which reads them back with <see cref="Parse"/>.
/// </summary>
public sealed partial record JumpListCommand(JumpListAction Action, string? PlaylistId = null)
{
    private const string PlaylistSwitch = "--play-playlist=";

    public static JumpListCommand PlayPause { get; } = new(JumpListAction.PlayPause);

    public static JumpListCommand Next { get; } = new(JumpListAction.Next);

    public static JumpListCommand ShuffleAll { get; } = new(JumpListAction.ShuffleAll);

    /// <summary>Gets the command-line arguments that start this command.</summary>
    public string Arguments => Action switch
    {
        JumpListAction.PlayPause => "--play-pause",
        JumpListAction.Next => "--next",
        JumpListAction.ShuffleAll => "--shuffle-all",
        _ => PlaylistSwitch + PlaylistId,
    };

    public static JumpListCommand ForPlaylist(string playlistId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        if (!IsPlaylistId(playlistId))
        {
            throw new ArgumentException("A playlist ID has only letters, digits and dashes.", nameof(playlistId));
        }

        return new JumpListCommand(JumpListAction.PlayPlaylist, playlistId);
    }

    /// <summary>
    /// Finds the command in a command line, or <see langword="null"/> when it has none. The line may start with
    /// the program's path (an unpackaged start) or hold only the arguments (a packaged one).
    /// </summary>
    public static JumpListCommand? Parse(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        foreach (var token in commandLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim('"')))
        {
            switch (token)
            {
                case "--play-pause":
                    return PlayPause;
                case "--next":
                    return Next;
                case "--shuffle-all":
                    return ShuffleAll;
            }

            if (token.StartsWith(PlaylistSwitch, StringComparison.Ordinal) && IsPlaylistId(token[PlaylistSwitch.Length..]))
            {
                return new JumpListCommand(JumpListAction.PlayPlaylist, token[PlaylistSwitch.Length..]);
            }
        }

        return null;
    }

    private static bool IsPlaylistId(string id) => PlaylistIdPattern().IsMatch(id);

    [GeneratedRegex("^[0-9A-Za-z-]{1,64}$")]
    private static partial Regex PlaylistIdPattern();
}
