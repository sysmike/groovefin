using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Models;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class DownloadsViewModelTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly FakeDownloadSource _source = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly FakeNetworkStatus _network = new();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly IFolderPicker _picker = Substitute.For<IFolderPicker>();
    private readonly string _chosen = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly DownloadManager _downloads;
    private readonly DownloadsViewModel _sut;

    public DownloadsViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Downloads_Status").Returns("{0} of {1} · {2}");
        _strings.GetString("Downloads_FormatAac").Returns("AAC {0}");
        _strings.GetString("Downloads_Failure").Returns("{0}: {1}");
        _strings.GetString("Size_Megabytes").Returns("{0} MB");
        _strings.GetString("Downloads_LargeTitle").Returns("Download {0}?");
        _strings.GetString("Size_Gigabytes").Returns("{0} GB");
        _client.Connection.Returns(new ServerConnection(new Uri("http://media:8096"), "user1", "token"));
        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([new Track { Id = "a", Name = "Song a" }, new Track { Id = "b", Name = "Song b" }]);
        var store = new JsonDownloadStore(
            new DownloadStoreOptions(_directory) { FreeSpace = _ => long.MaxValue, IsLocal = path => !path.Contains("network", StringComparison.Ordinal) },
            NullLogger<JsonDownloadStore>.Instance);
        _downloads = new DownloadManager(
            _client, _library, Substitute.For<IPlaylistService>(), _source, store, _settings, _network, Substitute.For<IServerManager>(),
            new WeakReferenceMessenger(), new FakeTimeProvider(), NullLogger<DownloadManager>.Instance);
        _sut = new DownloadsViewModel(_downloads, _settings, _network, _dialogs, _strings, new ImmediateDispatcher(), _picker);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _downloads.Dispose();
        foreach (var folder in new[] { _directory, _chosen }.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task The_switches_and_the_format_change_the_settings()
    {
        _sut.FavouriteSongs = true;
        _sut.Playlists = true;
        _sut.AllowMetered = true;
        _sut.FormatIndex = 2;
        await WaitUntilAsync(() => _settings.Current.Downloads is { FavouriteSongs: true, Playlists: true });

        _settings.Current.Downloads.Should().Be(new DownloadSettings
        {
            FavouriteSongs = true,
            Playlists = true,
            AllowMetered = true,
            Format = DownloadFormatKind.Aac,
            AacBitrateKbps = 256,
        });
        _sut.FormatOptions.Should().Equal("Downloads_FormatOriginal", "AAC 320", "AAC 256", "AAC 192", "AAC 128");

        _sut.FormatIndex = 0;
        _settings.Current.Downloads.Format.Should().Be(DownloadFormatKind.Original);
    }

    [Fact]
    public async Task The_status_shows_the_songs_on_disk_and_the_failed_ones()
    {
        _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteSongs = true } });
        _source.Failures["b"] = new JellyfinException("Forbidden", System.Net.HttpStatusCode.Forbidden);

        await _downloads.SyncAsync();

        _sut.StatusText.Should().Be("1 of 2 · 0 MB");
        _sut.Failures.Should().Equal("Song b: HTTP 403");
        _sut.HasFailures.Should().BeTrue();
        _sut.HasActivity.Should().BeFalse();
    }

    [Fact]
    public void Without_downloads_the_status_says_so()
    {
        _sut.StatusText.Should().Be("Downloads_StatusNone");
        _sut.RemoveAllCommand.CanExecute(null).Should().BeFalse();
        _sut.SyncNowCommand.CanExecute(null).Should().BeFalse("no collection is on");
    }

    [Fact]
    public async Task Sync_now_on_a_metered_connection_asks_first()
    {
        _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteSongs = true } });
        _network.IsMetered = true;
        _dialogs.ConfirmAsync("Downloads_MeteredTitle", "Downloads_MeteredMessage", "Downloads_MeteredConfirm", "Dialog_Cancel").Returns(false, true);

        await _sut.SyncNowCommand.ExecuteAsync(null);
        _source.Requests.Should().BeEmpty();

        await _sut.SyncNowCommand.ExecuteAsync(null);
        _source.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Turning_on_a_large_collection_asks_first_and_cancel_leaves_it_off()
    {
        _library.GetTrackFilesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, TrackFile> { ["a"] = new("flac", 800L << 20), ["b"] = new("flac", 800L << 20) });
        var asked = new TaskCompletionSource<bool>();
        _dialogs.ConfirmAsync("Download 2?", Arg.Any<string>(), "Downloads_LargeConfirm", "Dialog_Cancel").Returns(asked.Task);

        _sut.FavouriteSongs = true;

        _sut.FavouriteSongs.Should().BeTrue("the switch stays on while the size is checked");
        _settings.Current.Downloads.FavouriteSongs.Should().BeFalse();
        asked.SetResult(false);
        await WaitUntilAsync(() => !_sut.FavouriteSongs);
        _sut.FavouriteSongs.Should().BeFalse();
        _settings.Current.Downloads.FavouriteSongs.Should().BeFalse();
    }

    [Fact]
    public async Task Turning_on_a_small_collection_asks_nothing()
    {
        _sut.FavouriteSongs = true;
        await WaitUntilAsync(() => _settings.Current.Downloads.FavouriteSongs);

        _settings.Current.Downloads.FavouriteSongs.Should().BeTrue();
        await _dialogs.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default!, default!, default!);
    }

    [Fact]
    public void Turning_a_collection_off_asks_nothing()
    {
        _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteArtists = true } });

        _sut.FavouriteArtists = false;

        _settings.Current.Downloads.FavouriteArtists.Should().BeFalse();
    }

    [Fact]
    public async Task Remove_all_asks_then_deletes_and_turns_the_switches_off()
    {
        _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteSongs = true, Playlists = true } });
        await _downloads.SyncAsync();
        _dialogs.ConfirmAsync("Downloads_RemoveTitle", Arg.Any<string>(), "Downloads_RemoveConfirm", "Dialog_Cancel").Returns(true);

        await _sut.RemoveAllCommand.ExecuteAsync(null);

        _settings.Current.Downloads.AnyCollection.Should().BeFalse();
        _downloads.Status.Bytes.Should().Be(0);
        _sut.StatusText.Should().Be("Downloads_StatusNone");
    }

    [Fact]
    public async Task Changing_the_folder_moves_the_downloads_and_shows_it()
    {
        _picker.PickFolderAsync().Returns(_chosen);
        _sut.FolderText.Should().Be(_directory);
        _sut.UseDefaultFolderCommand.CanExecute(null).Should().BeFalse();

        await _sut.ChangeFolderCommand.ExecuteAsync(null);

        _sut.FolderText.Should().Be(Path.Join(Path.GetFullPath(_chosen), DownloadStoreOptions.FolderName));
        _sut.IsCustomFolder.Should().BeTrue();
        _sut.IsMoving.Should().BeFalse();
        _sut.HasMoveMessage.Should().BeFalse();
        _sut.UseDefaultFolderCommand.CanExecute(null).Should().BeTrue();

        await _sut.UseDefaultFolderCommand.ExecuteAsync(null);

        _sut.FolderText.Should().Be(_directory);
        _sut.IsCustomFolder.Should().BeFalse();
    }

    [Fact]
    public async Task A_cancelled_picker_changes_nothing()
    {
        _picker.PickFolderAsync().Returns(Task.FromResult<string?>(null));

        await _sut.ChangeFolderCommand.ExecuteAsync(null);

        _sut.FolderText.Should().Be(_directory);
        _settings.Current.Downloads.Folder.Should().BeNull();
    }

    [Fact]
    public async Task A_network_folder_says_why_the_downloads_stay()
    {
        _picker.PickFolderAsync().Returns(Path.Join(_chosen, "network"));

        await _sut.ChangeFolderCommand.ExecuteAsync(null);

        _sut.MoveMessage.Should().Be("Downloads_MoveNotLocal");
        _sut.FolderText.Should().Be(_directory);
        _settings.Current.Downloads.Folder.Should().BeNull();
    }
}
