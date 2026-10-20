using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Servers;
using Groovefin.Core.Session;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Session;

public sealed class SessionPersistenceTests : IDisposable
{
    private static readonly ServerProfile Home = new() { Id = "srv", Name = "Home", Address = new Uri("http://home:8096/"), UserId = "user" };

    private readonly FakeTimeProvider _time = new();
    private readonly QueueManager _queue = new(new Random(1));
    private readonly FakePlaybackEngine _engine;
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly InMemorySettingsService _settings = new();
    private readonly ISessionStore _store = Substitute.For<ISessionStore>();
    private readonly SessionPersistence _sut;

    public SessionPersistenceTests()
    {
        _engine = new FakePlaybackEngine(time: _time);
        _player.Queue.Returns(_queue);
        _player.Engine.Returns(_engine);
        _servers.IsSignedIn.Returns(true);
        _servers.ActiveServer.Returns(Home);
        _sut = new SessionPersistence(_player, _servers, _settings, _store, new ImmediateDispatcher(), _time);
    }

    public void Dispose() => _sut.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track[] Tracks(params string[] ids) => ids.Select(id => new Track { Id = id, Name = id }).ToArray();

    private static SavedSession Saved(string serverId = "srv", string userId = "user") => new()
    {
        ServerId = serverId,
        UserId = userId,
        Queue = new QueueSnapshot { Tracks = Tracks("a", "b"), CurrentIndex = 1 },
        Position = TimeSpan.FromSeconds(42),
    };

    [Fact]
    public async Task The_saved_queue_is_restored_paused_at_the_position()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(Saved());

        await _sut.RestoreAsync(Ct);

        _queue.Items.Select(i => i.Track.Id).Should().Equal("a", "b");
        _queue.Current!.Track.Id.Should().Be("b");
        _player.Received(1).Resume(TimeSpan.FromSeconds(42));
    }

    [Theory]
    [InlineData("other", "user")]
    [InlineData("srv", "someone-else")]
    public async Task Another_server_or_user_starts_empty(string serverId, string userId)
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(Saved(serverId, userId));

        await _sut.RestoreAsync(Ct);

        _queue.Items.Should().BeEmpty();
        _player.DidNotReceiveWithAnyArgs().Resume(default);
    }

    [Fact]
    public async Task With_resuming_off_nothing_is_restored()
    {
        _settings.Update(s => s with { Playback = s.Playback with { ResumeSession = false } });
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(Saved());

        await _sut.RestoreAsync(Ct);

        _queue.Items.Should().BeEmpty();
        await _store.DidNotReceive().LoadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Signed_out_nothing_is_restored()
    {
        _servers.IsSignedIn.Returns(false);
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(Saved());

        await _sut.RestoreAsync(Ct);

        _queue.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Queue_changes_are_saved_after_a_short_delay()
    {
        await _sut.RestoreAsync(Ct);

        _queue.PlayNow(Tracks("a", "b"));
        _queue.Next();
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Ct);
        _time.Advance(SessionPersistence.SaveDelay);

        await _store.Received(1).SaveAsync(
            Arg.Is<SavedSession>(s => s.ServerId == "srv" && s.UserId == "user" && s.Queue.CurrentIndex == 1 && s.Queue.Tracks.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_position_is_saved_at_most_every_interval()
    {
        await _sut.RestoreAsync(Ct);
        _queue.PlayNow(Tracks("a"));
        _engine.Load(new PlaybackSource(_queue.Current!.Id, new Uri("http://stream/a"), _queue.Current.Track));

        // The queue change is saved first; count from there.
        _time.Advance(SessionPersistence.SaveDelay);
        _store.ClearReceivedCalls();

        _time.Advance(TimeSpan.FromSeconds(5));
        _engine.SimulatePosition(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(3));
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Ct);

        _time.Advance(SessionPersistence.PositionInterval);
        _engine.SimulatePosition(TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(3));

        await _store.Received().SaveAsync(Arg.Is<SavedSession>(s => s.Position == TimeSpan.FromSeconds(20)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_queue_or_resuming_off_removes_the_saved_session()
    {
        await _sut.SaveAsync();
        await _store.Received(1).ClearAsync(Arg.Any<CancellationToken>());

        _settings.Update(s => s with { Playback = s.Playback with { ResumeSession = false } });
        await _sut.RestoreAsync(Ct);
        _settings.Update(s => s with { Playback = s.Playback with { ResumeSession = true } });
        _settings.Update(s => s with { Playback = s.Playback with { ResumeSession = false } });

        await _store.Received(2).ClearAsync(Arg.Any<CancellationToken>());
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Ct);
    }
}
