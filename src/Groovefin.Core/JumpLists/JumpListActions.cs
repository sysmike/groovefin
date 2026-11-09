using Groovefin.Core.Api;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;

namespace Groovefin.Core.JumpLists;

/// <summary>Runs a jump list entry's command in the app (#406). Loading failures are shown, as for the play buttons.</summary>
public sealed class JumpListActions(IPlayer player, ILibraryService library, IPlaylistService playlists, IErrorReporter errors)
{
    /// <summary>How many random songs Shuffle all plays.</summary>
    public const int ShuffleAllCount = 200;

    public async Task RunAsync(JumpListCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command.Action)
        {
            case JumpListAction.PlayPause:
                player.TogglePlayPause();
                return;
            case JumpListAction.Next:
                player.Next();
                return;
        }

        try
        {
            IReadOnlyList<Models.Track> tracks = command.Action == JumpListAction.ShuffleAll
                ? (await library.GetTracksAsync(new TrackQuery { SortBy = TrackSortField.Random, Limit = ShuffleAllCount })).Items
                : [.. (await playlists.GetEntriesAsync(command.PlaylistId!)).Select(e => e.Track)];
            if (tracks.Count > 0)
            {
                player.Play(tracks);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.Playback);
        }
    }
}
