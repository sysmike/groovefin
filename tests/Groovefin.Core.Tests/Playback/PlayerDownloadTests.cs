using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ReturnsExtensions;

namespace Groovefin.Core.Tests.Playback;

/// <summary>Downloaded songs play from their files, and offline the others are skipped (#333).</summary>
public sealed class PlayerDownloadTests : IDisposable
{
    private readonly QueueManager _queue = new(new Random(1));
    private readonly FakePlaybackEngine _engine = new();
    private readonly IStreamUrlProvider _streams = Substitute.For<IStreamUrlProvider>();
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IOfflineLibrary _offline = Substitute.For<IOfflineLibrary>();
    private readonly FakeNetworkStatus _network = new();
    private readonly Player _sut;

    public PlayerDownloadTests()
    {
        _client.Connection.Returns(new ServerConnection(new Uri("http://media:8096/"), "user", "token"));
        _streams.GetStreamUrl(Arg.Any<string>(), Arg.Any<StreamOptions>()).Returns(call => new Uri($"http://stream/{call.Arg<string>()}"));
        _offline.FileOf(Arg.Any<string>()).ReturnsNull();
        _sut = new Player(_queue, _engine, _streams, _client, new InMemorySettingsService(), _errors, NullLogger<Player>.Instance, _offline, _network);
    }

    public void Dispose() => _sut.Dispose();

    private static Track[] Tracks(params string[] ids) => ids.Select(id => new Track { Id = id, Name = id }).ToArray();

    private void Downloaded(string id) => _offline.FileOf(id).Returns(Path.Join(Path.GetTempPath(), $"{id}.flac"));

    [Fact]
    public void A_downloaded_song_plays_from_its_file_also_online()
    {
        Downloaded("a");

        _sut.Play(Tracks("a", "b"));

        _engine.Current!.Url.Should().Be(new Uri(Path.Join(Path.GetTempPath(), "a.flac")));
        _engine.Current.Url.IsFile.Should().BeTrue();
        _engine.Current.UrlAt.Should().BeNull("seeking in a file needs no new URL");
        _streams.DidNotReceive().GetStreamUrl("a", Arg.Any<StreamOptions>());
        _engine.Next!.Url.Should().Be(new Uri("http://stream/b"), "online, a song without a file streams");
    }

    [Fact]
    public void A_downloaded_song_plays_without_a_server_connection()
    {
        _client.Connection.ReturnsNull();
        Downloaded("a");

        _sut.Play(Tracks("a"));

        _engine.Current!.Url.IsFile.Should().BeTrue();
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public void Offline_the_songs_that_are_not_downloaded_are_skipped()
    {
        _network.IsOnline = false;
        Downloaded("c");

        _sut.Play(Tracks("a", "b", "c", "d"));

        _engine.Current!.Track.Id.Should().Be("c");
        _streams.DidNotReceiveWithAnyArgs().GetStreamUrl(default!, default!);
        _engine.Next.Should().BeNull("d is not downloaded");
    }

    [Fact]
    public void Offline_with_none_of_the_songs_downloaded_it_stops_and_says_so()
    {
        _network.IsOnline = false;

        _sut.Play(Tracks("a", "b"));

        _engine.Current.Should().BeNull();
        _errors.Received(1).Report(Arg.Any<NotDownloadedException>(), Operations.Playback);
    }

    [Fact]
    public void Offline_the_end_of_a_song_skips_to_the_next_downloaded_one()
    {
        _network.IsOnline = false;
        Downloaded("a");
        Downloaded("c");
        _sut.Play(Tracks("a", "b", "c"));

        _engine.SimulateEnded();

        _engine.Current!.Track.Id.Should().Be("c");
    }
}
