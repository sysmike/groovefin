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
    private readonly DownloadManager _downloads;
    private readonly DownloadsViewModel _sut;

    public DownloadsViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Downloads_Status").Returns("{0} of {1} · {2}");
        _strings.GetString("Downloads_FormatAac").Returns("AAC {0}");
        _strings.GetString("Downloads_Failure").Returns("{0}: {1}");
        _strings.GetString("Size_Megabytes").Returns("{0} MB");
        _client.Connection.Returns(new ServerConnection(new Uri("http://media:8096"), "user1", "token"));
        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([new Track { Id = "a", Name = "Song a" }, new Track { Id = "b", Name = "Song b" }]);
        var store = new JsonDownloadStore(new DownloadStoreOptions(_directory) { FreeSpace = _ => long.MaxValue }, NullLogger<JsonDownloadStore>.Instance);
        _downloads = new DownloadManager(
            _client, _library, Substitute.For<IPlaylistService>(), _source, store, _settings, _network, Substitute.For<IServerManager>(),
            new WeakReferenceMessenger(), new FakeTimeProvider(), NullLogger<DownloadManager>.Instance);
        _sut = new DownloadsViewModel(_downloads, _settings, _network, _dialogs, _strings, new ImmediateDispatcher());
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

    [Fact]
    public void The_switches_and_the_format_change_the_settings()
    {
        _sut.FavouriteSongs = true;
        _sut.Playlists = true;
        _sut.AllowMetered = true;
        _sut.FormatIndex = 2;

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
}
