using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Favourites;

/// <summary>A playlist's name or favourite state changed; sent by the page that changed it (#297).</summary>
public sealed record PlaylistChangedMessage(Playlist Playlist);

/// <summary>A playlist was deleted.</summary>
public sealed record PlaylistDeletedMessage(string PlaylistId);

/// <summary>
/// The favourite playlists the navigation pane lists after its sections (#297), by name. Loaded when a user
/// signs in, emptied on sign-out and when another server or user becomes active, and kept current by
/// <see cref="PlaylistChangedMessage"/>, <see cref="PlaylistDeletedMessage"/> and unfavouriting. It loads in
/// the background, so a failure is logged, not shown: the pane then lists no playlists. Use
/// <see cref="Items"/> from the UI thread.
/// </summary>
public sealed class FavouritePlaylists : IDisposable
{
    private static readonly Comparer<Playlist> ByName = Comparer<Playlist>.Create(
        (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

    private readonly IPlaylistService _playlists;
    private readonly IServerManager _servers;
    private readonly FavouritesService _favourites;
    private readonly IMessenger _messenger;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<FavouritePlaylists> _logger;

    /// <summary>The server and user the list belongs to, or <see langword="null"/> when nobody is signed in.</summary>
    private string? _owner;

    /// <summary>Counts the loads, so that one started for an earlier user does not fill the list.</summary>
    private int _load;

    public FavouritePlaylists(
        IPlaylistService playlists,
        IServerManager servers,
        FavouritesService favourites,
        IMessenger messenger,
        IDispatcher dispatcher,
        ILogger<FavouritePlaylists> logger)
    {
        _playlists = playlists;
        _servers = servers;
        _favourites = favourites;
        _messenger = messenger;
        _dispatcher = dispatcher;
        _logger = logger;
        _servers.Changed += OnServersChanged;
        messenger.Register<FavouritePlaylists, PlaylistChangedMessage>(this, static (r, m) => r.Update(m.Playlist));
        messenger.Register<FavouritePlaylists, PlaylistDeletedMessage>(this, static (r, m) => r.Remove(m.PlaylistId));
        messenger.Register<FavouritePlaylists, FavouriteChangedMessage>(this, static (r, m) => r.OnFavouriteChanged(m));
    }

    /// <summary>Gets the favourite playlists, by name.</summary>
    public ObservableCollection<Playlist> Items { get; } = [];

    /// <summary>Loads the list for the user signed in now, if any. Called once when the window is ready.</summary>
    public Task StartAsync() => FollowAsync();

    public void Dispose()
    {
        _servers.Changed -= OnServersChanged;
        _messenger.UnregisterAll(this);
    }

    /// <summary>The server manager raises this after network calls, on a thread-pool thread.</summary>
    private void OnServersChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.HasThreadAccess ? FollowAsync() : _dispatcher.EnqueueAsync(() => _ = FollowAsync());
    }

    /// <summary>Starts again when someone else is signed in now; nothing changes for other server changes.</summary>
    private async Task FollowAsync()
    {
        var owner = _servers.IsSignedIn && _servers.ActiveServer is { } server ? $"{server.Id}/{server.UserId}" : null;
        if (owner == _owner)
        {
            return;
        }

        _owner = owner;
        var load = ++_load;
        Items.Clear();
        if (owner is null)
        {
            return;
        }

        IReadOnlyList<Playlist> all;
        try
        {
            all = await _playlists.GetPlaylistsAsync();
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Loading the favourite playlists failed");
            return;
        }

        if (load != _load)
        {
            return;
        }

        foreach (var playlist in all.Where(p => _favourites.IsFavourite(p.Id, p.IsFavorite)).Order(ByName))
        {
            Items.Add(playlist);
        }

        _logger.LogInformation("{Count} of {Total} playlists are favourites", Items.Count, all.Count);
    }

    /// <summary>A favourite takes its place by name (again, after a rename); any other playlist leaves.</summary>
    private void Update(Playlist playlist)
    {
        Remove(playlist.Id);
        if (_owner is null || !_favourites.IsFavourite(playlist.Id, playlist.IsFavorite))
        {
            _logger.LogInformation("Playlist {PlaylistId} is not in the navigation pane (signed in: {SignedIn})", playlist.Id, _owner is not null);
            return;
        }

        var index = 0;
        while (index < Items.Count && ByName.Compare(Items[index], playlist) <= 0)
        {
            index++;
        }

        Items.Insert(index, playlist);
        _logger.LogInformation("Playlist {PlaylistId} is in the navigation pane at {Index}", playlist.Id, index);
    }

    private void Remove(string playlistId)
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].Id == playlistId)
            {
                Items.RemoveAt(i);
            }
        }
    }

    /// <summary>Unfavouriting needs only the ID; a new favourite comes with its <see cref="PlaylistChangedMessage"/>.</summary>
    private void OnFavouriteChanged(FavouriteChangedMessage message)
    {
        if (!message.IsFavourite)
        {
            Remove(message.ItemId);
        }
    }
}
