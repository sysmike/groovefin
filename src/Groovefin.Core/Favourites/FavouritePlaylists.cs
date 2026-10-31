using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Favourites;

/// <summary>A playlist's name, favourite state or details changed; sent by the page that changed it (#297, #299).</summary>
public sealed record PlaylistChangedMessage(Playlist Playlist);

/// <summary>A playlist was deleted.</summary>
public sealed record PlaylistDeletedMessage(string PlaylistId);

/// <summary>Tracks were added to a playlist from a menu or by drag and drop; its details changed (#299).</summary>
public sealed record PlaylistTracksAddedMessage(string PlaylistId);

/// <summary>
/// The favourite playlists the navigation pane lists after its sections (#297), by name until the user
/// rearranges them (#306): their order is kept per server and user, and favourites not in it follow by name.
/// Loaded when a user signs in, emptied on sign-out and when another server or user becomes active, and kept
/// current by <see cref="PlaylistChangedMessage"/>, <see cref="PlaylistDeletedMessage"/>, unfavouriting and,
/// for the track count and length the pane shows (#299), <see cref="PlaylistTracksAddedMessage"/>. It loads
/// in the background, so a failure is logged, not shown: the pane then lists no playlists. Use
/// <see cref="Items"/> from the UI thread.
/// </summary>
public sealed class FavouritePlaylists : IDisposable
{
    private static readonly Comparer<Playlist> ByName = Comparer<Playlist>.Create(
        (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

    private readonly IPlaylistService _playlists;
    private readonly IServerManager _servers;
    private readonly FavouritesService _favourites;
    private readonly ISettingsService _settings;
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
        ISettingsService settings,
        IMessenger messenger,
        IDispatcher dispatcher,
        ILogger<FavouritePlaylists> logger)
    {
        _playlists = playlists;
        _servers = servers;
        _favourites = favourites;
        _settings = settings;
        _messenger = messenger;
        _dispatcher = dispatcher;
        _logger = logger;
        _servers.Changed += OnServersChanged;
        messenger.Register<FavouritePlaylists, PlaylistChangedMessage>(this, static (r, m) => r.Update(m.Playlist));
        messenger.Register<FavouritePlaylists, PlaylistDeletedMessage>(this, static (r, m) => r.Remove(m.PlaylistId));
        messenger.Register<FavouritePlaylists, FavouriteChangedMessage>(this, static (r, m) => r.OnFavouriteChanged(m));
        messenger.Register<FavouritePlaylists, PlaylistTracksAddedMessage>(this, static (r, m) => _ = r.ReloadAsync(m.PlaylistId));
    }

    /// <summary>Gets the favourite playlists, in the user's order.</summary>
    public ObservableCollection<Playlist> Items { get; } = [];

    /// <summary>Loads the list for the user signed in now, if any. Called once when the window is ready.</summary>
    public Task StartAsync() => FollowAsync();

    /// <summary>Gets a value indicating whether the favourite can move <paramref name="offset"/> places (Move up, Move down).</summary>
    public bool CanMove(string playlistId, int offset)
    {
        var index = IndexOf(playlistId);
        return index >= 0 && offset != 0 && index + offset >= 0 && index + offset < Items.Count;
    }

    /// <summary>Moves the favourite <paramref name="offset"/> places up (negative) or down, as far as it goes.</summary>
    public void MoveBy(string playlistId, int offset)
    {
        if (IndexOf(playlistId) is var index and >= 0)
        {
            MoveTo(playlistId, index + offset);
        }
    }

    /// <summary>A favourite dropped on another one goes above it, or below it with <paramref name="after"/>.</summary>
    public void MoveNextTo(string playlistId, string targetId, bool after)
    {
        var from = IndexOf(playlistId);
        var target = IndexOf(targetId);
        if (from < 0 || target < 0 || from == target)
        {
            return;
        }

        var to = after ? target + 1 : target;
        MoveTo(playlistId, from < to ? to - 1 : to);
    }

    /// <summary>
    /// Moves the favourite to <paramref name="index"/> and keeps the whole list as this server and user's
    /// order (#306): favourites added later go below it.
    /// </summary>
    public void MoveTo(string playlistId, int index)
    {
        var from = IndexOf(playlistId);
        if (_owner is null || from < 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, Items.Count - 1);
        if (index == from)
        {
            return;
        }

        Items.Move(from, index);
        SaveOrder([.. Items.Select(p => p.Id)]);
        _logger.LogInformation("Playlist {PlaylistId} moved to {Index} in the navigation pane", playlistId, index);
    }

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

        foreach (var playlist in all.Where(p => _favourites.IsFavourite(p.Id, p.IsFavorite)).Order(Ordering()))
        {
            Items.Add(playlist);
        }

        _logger.LogInformation("{Count} of {Total} playlists are favourites", Items.Count, all.Count);
    }

    /// <summary>
    /// A favourite takes its place in the user's order, or by name after it; one that keeps its place is
    /// replaced there, so its item stays in the pane (#299). Any other playlist leaves.
    /// </summary>
    private void Update(Playlist playlist)
    {
        var current = IndexOf(playlist.Id);
        if (_owner is null || !_favourites.IsFavourite(playlist.Id, playlist.IsFavorite))
        {
            if (current >= 0)
            {
                Items.RemoveAt(current);
                Forget(playlist.Id);
                _logger.LogInformation("Playlist {PlaylistId} left the navigation pane", playlist.Id);
            }

            return;
        }

        var ordering = Ordering();
        var target = Items.Count(p => p.Id != playlist.Id && ordering.Compare(p, playlist) <= 0);
        if (current == target)
        {
            Items[current] = playlist;
            return;
        }

        if (current >= 0)
        {
            Items.RemoveAt(current);
        }

        Items.Insert(target, playlist);
        _logger.LogInformation("Playlist {PlaylistId} is in the navigation pane at {Index}", playlist.Id, target);
    }

    /// <summary>Loads a favourite again after tracks were added to it, for its count and length.</summary>
    private async Task ReloadAsync(string playlistId)
    {
        if (IndexOf(playlistId) < 0)
        {
            return;
        }

        var load = _load;
        Playlist playlist;
        try
        {
            playlist = await _playlists.GetPlaylistAsync(playlistId);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Loading playlist {PlaylistId} for the navigation pane failed", playlistId);
            return;
        }

        // Still the same user's list, and still in it (not unfavourited or deleted meanwhile).
        if (load == _load && IndexOf(playlistId) >= 0)
        {
            Update(playlist);
        }
    }

    private int IndexOf(string playlistId)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Id == playlistId)
            {
                return i;
            }
        }

        return -1;
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

        Forget(playlistId);
    }

    /// <summary>Gets this server and user's order (#306); empty until they rearrange the list.</summary>
    private IReadOnlyList<string> Order =>
        _owner is not null && _settings.Current.Appearance.FavouritePlaylistOrder.TryGetValue(_owner, out var order) ? order : [];

    /// <summary>The playlists in the user's order first, then the others by name.</summary>
    private Comparer<Playlist> Ordering()
    {
        var order = Order;
        int Rank(Playlist playlist)
        {
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i] == playlist.Id)
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        return Comparer<Playlist>.Create((a, b) =>
        {
            var byOrder = Rank(a).CompareTo(Rank(b));
            return byOrder != 0 ? byOrder : ByName.Compare(a, b);
        });
    }

    /// <summary>A playlist no longer a favourite leaves the order: favourited again, it goes at the end.</summary>
    private void Forget(string playlistId)
    {
        var order = Order;
        if (order.Contains(playlistId))
        {
            SaveOrder([.. order.Where(id => id != playlistId)]);
        }
    }

    private void SaveOrder(IReadOnlyList<string> order)
    {
        if (_owner is not { } owner)
        {
            return;
        }

        _settings.Update(s =>
        {
            var orders = new Dictionary<string, IReadOnlyList<string>>(s.Appearance.FavouritePlaylistOrder, StringComparer.Ordinal) { [owner] = order };
            if (order.Count == 0)
            {
                orders.Remove(owner);
            }

            return s with { Appearance = s.Appearance with { FavouritePlaylistOrder = orders } };
        });
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
