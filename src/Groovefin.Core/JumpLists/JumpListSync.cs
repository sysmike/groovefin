using System.Collections.Specialized;
using Groovefin.Core.Favourites;
using Groovefin.Core.Services;

namespace Groovefin.Core.JumpLists;

/// <summary>
/// Keeps the jump list (#406): Play/Pause, Next track and Shuffle all under Tasks, and the favourite playlists of the
/// navigation pane, in its order, in a group of their own. Lives as long as the app; use it from the UI thread.
/// </summary>
public sealed class JumpListSync : IDisposable
{
    /// <summary>Most playlists listed; Windows shows about ten entries per group anyway.</summary>
    public const int MaxPlaylists = 10;

    private readonly IJumpList _jumpList;
    private readonly FavouritePlaylists _favourites;
    private readonly IStringResources _strings;

    public JumpListSync(IJumpList jumpList, FavouritePlaylists favourites, IStringResources strings)
    {
        _jumpList = jumpList;
        _favourites = favourites;
        _strings = strings;
        _favourites.Items.CollectionChanged += OnFavouritesChanged;
        Update();
    }

    /// <summary>Gets the last update; it waits for the ones before it.</summary>
    internal Task LastUpdate { get; private set; } = Task.CompletedTask;

    public void Dispose() => _favourites.Items.CollectionChanged -= OnFavouritesChanged;

    private void OnFavouritesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    private void Update()
    {
        JumpListEntry[] tasks =
        [
            new(_strings.GetString("JumpList_PlayPause"), JumpListCommand.PlayPause),
            new(_strings.GetString("JumpList_Next"), JumpListCommand.Next),
            new(_strings.GetString("JumpList_ShuffleAll"), JumpListCommand.ShuffleAll),
        ];
        var playlists = _favourites.Items
            .Take(MaxPlaylists)
            .Select(p => new JumpListEntry(p.Name, JumpListCommand.ForPlaylist(p.Id)))
            .ToList();
        LastUpdate = AfterAsync(LastUpdate, tasks, _strings.GetString("JumpList_Playlists"), playlists);
    }

    /// <summary>One update after the other: the list loads its playlists one by one, and Windows' jump list is one file.</summary>
    private async Task AfterAsync(Task previous, JumpListEntry[] tasks, string group, List<JumpListEntry> playlists)
    {
        await previous;
        await _jumpList.SetAsync(tasks, group, playlists);
    }
}
