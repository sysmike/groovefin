using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Formatting;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>A playlist tile or row: image, name and "12 tracks · 1:02:15".</summary>
public sealed record PlaylistItem(Playlist Playlist, ImageRequest? Image, string Details)
{
    public string Name => Playlist.Name;

    public string AccessibleName => string.Join(", ", new[] { Name, Details }.Where(s => s.Length > 0));

    public static PlaylistItem Create(Playlist playlist, double imageWidth, double scale, IStringResources strings)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(strings);
        return new PlaylistItem(
            playlist,
            ImageRequest.Primary(playlist.Id, playlist.PrimaryImageTag, imageWidth, scale),
            DetailsText(playlist.TrackCount, playlist.Duration, strings));
    }

    /// <summary>"12 tracks · 1:02:15", or what of it is known.</summary>
    public static string DetailsText(int? trackCount, TimeSpan? duration, IStringResources strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        var count = trackCount is { } n
            ? strings.GetString(n == 1 ? "Album_TrackCountOne" : "Album_TrackCount").Replace("{0}", n.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
            : string.Empty;
        var length = duration is { } d && d > TimeSpan.Zero ? Durations.Track(d) : string.Empty;
        return string.Join(" · ", new[] { count, length }.Where(s => s.Length > 0));
    }
}

/// <summary>Playlists page: the user's audio playlists, and creating a new one.</summary>
public sealed partial class PlaylistsViewModel(
    IPlaylistService playlists,
    INavigationService navigation,
    IDialogService dialogs,
    IErrorReporter errors,
    IStringResources strings) : ObservableObject
{
    public const double TileWidth = 168;

    public ObservableCollection<PlaylistItem> Playlists { get; } = [];

    public double DisplayScale { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool LoadFailed { get; private set; }

    /// <summary>Gets a value indicating whether the user has no playlists (after a successful load).</summary>
    public bool IsEmpty => !IsLoading && !LoadFailed && Playlists.Count == 0;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        LoadFailed = false;
        try
        {
            var found = await playlists.GetPlaylistsAsync(cancellationToken);
            Playlists.Clear();
            foreach (var playlist in found)
            {
                Playlists.Add(PlaylistItem.Create(playlist, TileWidth, DisplayScale, strings));
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadPlaylists);
        }
        catch (OperationCanceledException)
        {
            // The page was closed.
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Open(PlaylistItem item) => navigation.NavigateTo(PageKeys.Playlist, item.Playlist.Id);

    /// <summary>Asks for a name, creates an empty playlist and opens it.</summary>
    [RelayCommand]
    private async Task CreateAsync()
    {
        var name = await dialogs.PromptAsync(
            strings.GetString("Playlist_CreateTitle"),
            strings.GetString("Playlist_NameLabel"),
            string.Empty,
            strings.GetString("Playlist_CreateButton"),
            strings.GetString("Dialog_Cancel"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var id = await playlists.CreateAsync(name, []);
            navigation.NavigateTo(PageKeys.Playlist, id);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            errors.Report(ex, Operations.EditPlaylist);
        }
    }
}
