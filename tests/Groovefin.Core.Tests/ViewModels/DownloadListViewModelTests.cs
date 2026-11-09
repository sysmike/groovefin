using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.ViewModels;

/// <summary>The Downloads page (#388).</summary>
public sealed class DownloadListViewModelTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly FakeDownloadSource _source = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly DownloadManager _downloads;
    private readonly DownloadListViewModel _sut;

    public DownloadListViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Downloads_ItemDownloaded").Returns("{0} songs · {1}");
        _strings.GetString("Downloads_ItemFailed").Returns("{0} failed: {1}: {2}");
        _strings.GetString("Downloads_KindAlbumBy").Returns("Album by {0}");
        _strings.GetString("Downloads_RemoveName").Returns("Remove {0}");
        _strings.GetString("Downloads_Space").Returns("{0} used, {1} free");
        _strings.GetString("Size_Megabytes").Returns("{0} MB");
        _strings.GetString("Size_Gigabytes").Returns("{0} GB");
        _client.Connection.Returns(new ServerConnection(new Uri("http://media:8096"), "user1", "token"));
        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([new Track { Id = "a", Name = "Song a" }]);
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Album>([new Album { Id = "al1", Name = "Discovery", AlbumArtist = "Daft Punk" }], 1, 0));
        _library.GetAlbumTracksAsync("al1", Arg.Any<CancellationToken>()).Returns([new Track { Id = "b", Name = "Song b" }, new Track { Id = "c", Name = "Song c" }]);
        _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteSongs = true, FavouriteAlbums = true } });
        var store = new JsonDownloadStore(new DownloadStoreOptions(_directory) { FreeSpace = _ => 2L << 30 }, NullLogger<JsonDownloadStore>.Instance);
        _downloads = new DownloadManager(
            _client, _library, _playlists, _source, store, _settings, new FakeNetworkStatus(), Substitute.For<IServerManager>(),
            new WeakReferenceMessenger(), new FakeTimeProvider(), NullLogger<DownloadManager>.Instance);
        _sut = new DownloadListViewModel(_downloads, _settings, _navigation, _dialogs, _strings, new ImmediateDispatcher());
    }

    public void Dispose()
    {
        _sut.Dispose();
        _downloads.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private DownloadItemRow Album => _sut.Items.Single(r => r.Kind == DownloadItemKind.Album);

    [Fact]
    public async Task Every_download_shows_with_its_kind_songs_and_size()
    {
        await _downloads.SyncAsync();

        await _sut.LoadAsync();

        _sut.Items.Select(r => r.Name).Should().Equal("Downloads_FavouriteSongs", "Discovery");
        Album.Detail.Should().Be("Album by Daft Punk");
        Album.StatusText.Should().Be("2 songs · 0 MB");
        Album.CanRetry.Should().BeFalse();
        Album.RemoveName.Should().Be("Remove Discovery");
        _sut.SpaceText.Should().Be("0 MB used, 2 GB free");
        _sut.HasItems.Should().BeTrue();
        _sut.IsEmpty.Should().BeFalse();
        _sut.HasRemoved.Should().BeFalse();
    }

    [Fact]
    public async Task Without_downloads_the_page_says_so()
    {
        _settings.Update(s => s with { Downloads = new DownloadSettings() });

        await _sut.LoadAsync();

        _sut.IsEmpty.Should().BeTrue();
        _sut.SyncNowCommand.CanExecute(null).Should().BeFalse();
        _sut.OpenSettingsCommand.Execute(null);
        _navigation.Received(1).NavigateTo(PageKeys.Settings);
    }

    [Fact]
    public async Task Remove_asks_first_and_then_removes_only_that_download()
    {
        await _downloads.SyncAsync();
        await _sut.LoadAsync();

        _dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(false);
        await Album.RemoveCommand.ExecuteAsync(null);
        _settings.Current.Downloads.Removed.Should().BeEmpty("the question was cancelled");

        _dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(true);
        await Album.RemoveCommand.ExecuteAsync(null);
        await _downloads.SyncAsync();

        _sut.Items.Should().ContainSingle().Which.Kind.Should().Be(DownloadItemKind.FavouriteSongs);
        _sut.Removed.Should().ContainSingle().Which.Name.Should().Be("Discovery");
        _sut.HasRemoved.Should().BeTrue();
        await _dialogs.Received().ConfirmAsync("Downloads_RemoveItemTitle", "Downloads_RemoveItemMessage", "Downloads_RemoveItemConfirm", "Dialog_Cancel");

        _sut.Removed[0].RestoreCommand.Execute(null);
        await _downloads.SyncAsync();

        _sut.Items.Select(r => r.Kind).Should().Equal(DownloadItemKind.FavouriteSongs, DownloadItemKind.Album);
        _sut.HasRemoved.Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_download_says_why_and_can_be_retried()
    {
        _source.Failures["c"] = new JellyfinException("Forbidden", HttpStatusCode.Forbidden);
        await _downloads.SyncAsync();
        await _sut.LoadAsync();

        Album.StatusText.Should().Be("1 failed: Song c: HTTP 403");
        Album.CanRetry.Should().BeTrue();
        var row = Album;

        _source.Failures.Remove("c");
        row.RetryCommand.Execute(null);
        await _downloads.SyncAsync();

        Album.Should().BeSameAs(row, "the row is updated in place, so the focus stays");
        row.CanRetry.Should().BeFalse();
        row.StatusText.Should().Be("2 songs · 0 MB");
    }
}
