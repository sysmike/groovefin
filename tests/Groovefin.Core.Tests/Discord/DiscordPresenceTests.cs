using Groovefin.Core.Api;
using Groovefin.Core.Discord;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Privacy;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Discord;

public sealed class DiscordPresenceTests : IDisposable
{
    private static readonly DiscordApplication App = new("1234");

    private readonly FakeTimeProvider _time;
    private readonly FakePlaybackEngine _engine;
    private readonly InMemorySettingsService _settings = new();
    private readonly FakeDiscordConnection _discord = new();
    private readonly PrivacyMode _privacy = new();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private DiscordPresence? _sut;

    public DiscordPresenceTests()
    {
        _time = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        _engine = new(time: _time);
        _library.GetAlbumAsync("al1", Arg.Any<CancellationToken>()).Returns(new Album
        {
            Id = "al1",
            Name = "Discovery",
            MusicBrainzReleaseGroupId = "48117b82-8d4a-3e11-8fe1-0ba7a6b2d9a5",
        });
    }

    public void Dispose() => _sut?.Dispose();

    private DiscordPresence Start(DiscordApplication? application = null) =>
        _sut = new DiscordPresence(_engine, _library, _settings, _privacy, _discord, application ?? App, _time, NullLogger<DiscordPresence>.Instance);

    private void TurnOn(bool on = true) => _settings.Update(s => s with { Playback = s.Playback with { DiscordPresence = on } });

    private static PlaybackSource Source(string name = "One More Time", string albumId = "al1") => new("q1", new Uri("https://server/Audio/1"), new Track
    {
        Id = "t1",
        Name = name,
        AlbumId = albumId,
        AlbumName = "Discovery",
        AlbumArtist = "Daft Punk",
        Artists = [new ArtistRef("a1", "Daft Punk"), new ArtistRef("a2", "Romanthony")],
        Duration = TimeSpan.FromMinutes(5),
    });

    private void Play(PlaybackSource? source = null)
    {
        _engine.Load(source ?? Source());
        _engine.SimulateState(PlaybackState.Playing);
    }

    [Fact]
    public void Off_by_default_nothing_reaches_Discord()
    {
        Start();

        Play();

        _discord.Connects.Should().Be(0);
        _discord.Sent.Should().BeEmpty();
    }

    [Fact]
    public void Turned_on_it_shows_title_artists_album_and_progress()
    {
        TurnOn();
        Start();

        Play();
        _engine.Seek(TimeSpan.FromSeconds(60));

        _discord.ApplicationId.Should().Be("1234");
        var shown = _discord.Sent[^1]!;
        shown.Details.Should().Be("One More Time");
        shown.State.Should().Be("Daft Punk, Romanthony");
        shown.LargeText.Should().Be("Discovery");
        shown.Start.Should().Be(_time.GetUtcNow() - TimeSpan.FromSeconds(60));
        shown.End.Should().Be(shown.Start + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Pausing_clears_the_status_and_playing_again_shows_it()
    {
        TurnOn();
        Start();
        Play();

        _engine.SimulateState(PlaybackState.Paused);
        var paused = _discord.Sent[^1];
        var connectedWhilePaused = _discord.IsConnected;
        _engine.SimulateState(PlaybackState.Playing);

        paused.Should().BeNull();
        connectedWhilePaused.Should().BeTrue();
        _discord.Connects.Should().Be(1);
        _discord.Sent[^1]!.Details.Should().Be("One More Time");
        _discord.Sent[^1]!.Start.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public void A_session_restored_paused_shows_nothing_until_it_plays()
    {
        TurnOn();
        Start();

        // "Resume where I left off" opens the track without playing it.
        _engine.Load(Source(), autoPlay: false);
        _engine.SimulateState(PlaybackState.Opening);
        _engine.SimulateState(PlaybackState.Paused);

        _discord.Connects.Should().Be(0);
        _discord.Sent.Should().BeEmpty();
    }

    [Fact]
    public void Opening_the_next_track_keeps_the_status()
    {
        TurnOn();
        Start();
        Play();

        _engine.Load(Source("Aerodynamic"));
        _engine.SimulateState(PlaybackState.Opening);

        _discord.Sent[^1]!.Details.Should().Be("Aerodynamic");
    }

    [Fact]
    public void Skipping_to_the_next_track_keeps_the_connection()
    {
        TurnOn();
        Start();
        Play();

        // A new source passes through paused before it plays (#228).
        _engine.Load(Source("Aerodynamic"));
        _engine.SimulateState(PlaybackState.Paused);
        _engine.SimulateState(PlaybackState.Opening);
        _engine.SimulateState(PlaybackState.Playing);

        _discord.Connects.Should().Be(1);
        _discord.IsConnected.Should().BeTrue();
        _discord.Sent[^1]!.Details.Should().Be("Aerodynamic");
    }

    [Fact]
    public void The_queue_ending_clears_the_status()
    {
        TurnOn();
        Start();
        Play();

        // The player stays on the last track, paused.
        _engine.SimulatePosition(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        _engine.SimulateState(PlaybackState.Paused);

        _discord.Sent[^1].Should().BeNull();
    }

    [Fact]
    public void Progress_reports_send_nothing_new_but_a_seek_does()
    {
        TurnOn();
        Start();
        Play();
        var count = _discord.Sent.Count;

        _time.Advance(TimeSpan.FromSeconds(1));
        _engine.SimulatePosition(TimeSpan.FromSeconds(1), TimeSpan.Zero);
        var afterProgress = _discord.Sent.Count;
        _engine.Seek(TimeSpan.FromSeconds(120));

        afterProgress.Should().Be(count);
        _discord.Sent.Count.Should().Be(count + 1);
        _discord.Sent[^1]!.Start.Should().Be(_time.GetUtcNow() - TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void Stopping_clears_the_status()
    {
        TurnOn();
        Start();
        Play();

        _engine.Stop();

        _discord.Sent[^1].Should().BeNull();
        _discord.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void Turning_it_off_clears_the_status()
    {
        TurnOn();
        Start();
        Play();

        TurnOn(false);

        _discord.Sent[^1].Should().BeNull();
        _discord.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void Turning_it_off_while_paused_lets_go_of_Discord()
    {
        TurnOn();
        Start();
        Play();
        _engine.SimulateState(PlaybackState.Paused);

        TurnOn(false);

        _discord.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void Privacy_mode_clears_the_status_and_lets_go_of_Discord_until_it_is_off()
    {
        TurnOn();
        Start();
        Play();

        _privacy.IsOn = true;
        var cleared = _discord.Sent[^1];
        var connected = _discord.IsConnected;
        _engine.Load(Source("Aerodynamic"));
        _engine.SimulateState(PlaybackState.Playing);
        var sentInPrivacyMode = _discord.Sent.Count;
        _privacy.IsOn = false;

        cleared.Should().BeNull();
        connected.Should().BeFalse();
        sentInPrivacyMode.Should().Be(2);
        _discord.Sent[^1]!.Details.Should().Be("Aerodynamic");
        _discord.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void Started_in_privacy_mode_nothing_reaches_Discord()
    {
        TurnOn();
        _privacy.IsOn = true;
        Start();

        Play();

        _discord.Connects.Should().Be(0);
        _discord.Sent.Should().BeEmpty();
    }

    [Fact]
    public void Without_Discord_it_stays_quiet_and_tries_again_later()
    {
        _discord.Running = false;
        TurnOn();
        Start();
        Play();
        var attempts = _discord.Connects;

        _engine.SimulateState(PlaybackState.Paused);
        var beforeDelay = _discord.Connects;
        _discord.Running = true;
        _time.Advance(DiscordPresence.RetryDelay);
        _engine.SimulateState(PlaybackState.Playing);

        attempts.Should().Be(1);
        beforeDelay.Should().Be(1);
        _discord.Connects.Should().Be(2);
        _discord.Sent[^1]!.Details.Should().Be("One More Time");
    }

    [Fact]
    public void When_Discord_closes_the_pipe_mid_song_the_status_comes_back()
    {
        TurnOn();
        Start();
        Play();

        _discord.Close();
        _time.Advance(TimeSpan.FromSeconds(1));
        _engine.SimulatePosition(TimeSpan.FromSeconds(1), TimeSpan.Zero);
        var beforeDelay = _discord.Connects;
        _time.Advance(DiscordPresence.ReconnectDelay);
        _engine.SimulatePosition(TimeSpan.FromSeconds(1) + DiscordPresence.ReconnectDelay, TimeSpan.Zero);

        beforeDelay.Should().Be(1);
        _discord.Connects.Should().Be(2);
        _discord.IsConnected.Should().BeTrue();
        _discord.Sent[^1]!.Details.Should().Be("One More Time");
    }

    [Fact]
    public void When_Discord_quits_it_reconnects_later()
    {
        TurnOn();
        Start();
        Play();
        _discord.BreakNextSend = true;

        _engine.Load(Source("Aerodynamic"));
        _engine.SimulateState(PlaybackState.Playing);
        _time.Advance(DiscordPresence.RetryDelay);
        _engine.SimulateState(PlaybackState.Playing);
        _engine.Seek(TimeSpan.FromSeconds(10));

        _discord.Connects.Should().Be(2);
        _discord.Sent[^1]!.Details.Should().Be("Aerodynamic");
    }

    [Fact]
    public void The_artists_link_to_the_first_on_Last_fm_and_the_album_to_MusicBrainz()
    {
        TurnOn();
        Start();

        Play();

        var shown = _discord.Sent[^1]!;
        shown.StateUrl.Should().Be(new Uri("https://www.last.fm/music/Daft+Punk"));
        shown.LargeUrl.Should().Be(new Uri("https://musicbrainz.org/release-group/48117b82-8d4a-3e11-8fe1-0ba7a6b2d9a5"));
    }

    [Fact]
    public async Task The_album_is_loaded_once_for_its_tracks()
    {
        TurnOn();
        Start();

        Play();
        Play(Source("Aerodynamic"));
        _engine.Seek(TimeSpan.FromSeconds(30));

        await _library.Received(1).GetAlbumAsync("al1", Arg.Any<CancellationToken>());
        _discord.Sent[^1]!.LargeUrl.Should().NotBeNull();
    }

    [Fact]
    public void The_status_shows_at_once_and_again_with_the_link_once_the_album_is_loaded()
    {
        var album = new TaskCompletionSource<Album>();
        _library.GetAlbumAsync("al4", Arg.Any<CancellationToken>()).Returns(album.Task);
        TurnOn();
        Start();

        Play(Source(albumId: "al4"));
        var before = _discord.Sent.Count;
        album.SetResult(new Album { Id = "al4", Name = "Discovery", MusicBrainzReleaseId = "b1a9c0e9-d987-4042-ae91-78d6a3267d69" });

        _discord.Sent[before - 1]!.LargeUrl.Should().BeNull();
        _discord.Sent.Should().HaveCount(before + 1);
        _discord.Sent[^1]!.LargeUrl.Should().Be(new Uri("https://musicbrainz.org/release/b1a9c0e9-d987-4042-ae91-78d6a3267d69"));
    }

    [Fact]
    public void An_album_loaded_after_the_next_one_started_sends_nothing()
    {
        var album = new TaskCompletionSource<Album>();
        _library.GetAlbumAsync("al4", Arg.Any<CancellationToken>()).Returns(album.Task);
        TurnOn();
        Start();

        Play(Source(albumId: "al4"));
        Play(Source("Aerodynamic"));
        var before = _discord.Sent.Count;
        album.SetResult(new Album { Id = "al4", Name = "Discovery", MusicBrainzReleaseId = "b1a9c0e9-d987-4042-ae91-78d6a3267d69" });

        _discord.Sent.Should().HaveCount(before);
        _discord.Sent[^1]!.Details.Should().Be("Aerodynamic");
    }

    [Fact]
    public void An_album_without_a_MusicBrainz_ID_has_no_link()
    {
        _library.GetAlbumAsync("al2", Arg.Any<CancellationToken>()).Returns(new Album { Id = "al2", Name = "Homework" });
        TurnOn();
        Start();

        Play(Source(albumId: "al2"));

        var shown = _discord.Sent[^1]!;
        shown.LargeUrl.Should().BeNull();
        shown.StateUrl.Should().NotBeNull();
    }

    [Fact]
    public void When_the_album_cannot_be_loaded_the_status_shows_without_its_link()
    {
        _library.GetAlbumAsync("al3", Arg.Any<CancellationToken>()).Returns<Album>(_ => throw new JellyfinException("Offline"));
        TurnOn();
        Start();

        Play(Source(albumId: "al3"));

        var shown = _discord.Sent[^1]!;
        shown.Details.Should().Be("One More Time");
        shown.LargeUrl.Should().BeNull();
    }

    [Fact]
    public async Task While_the_status_is_off_no_album_is_loaded()
    {
        Start();

        Play();

        await _library.DidNotReceive().GetAlbumAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_build_has_the_Groovefin_application()
    {
        DiscordApplication.Groovefin.IsSet.Should().BeTrue();
        DiscordApplication.Groovefin.Id.Should().MatchRegex("^[0-9]{17,20}$", "Discord IDs are snowflakes");
    }

    [Fact]
    public void A_build_without_an_application_shows_nothing()
    {
        TurnOn();
        Start(new DiscordApplication(string.Empty));

        Play();

        _discord.Connects.Should().Be(0);
    }

    private sealed class FakeDiscordConnection : IDiscordConnection
    {
        public bool Running { get; set; } = true;

        public bool BreakNextSend { get; set; }

        public bool IsConnected { get; private set; }

        public int Connects { get; private set; }

        public string? ApplicationId { get; private set; }

        public List<DiscordActivity?> Sent { get; } = [];

        public Task ConnectAsync(string applicationId, CancellationToken cancellationToken = default)
        {
            Connects++;
            if (!Running)
            {
                throw new IOException("Discord is not running.");
            }

            ApplicationId = applicationId;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task SetActivityAsync(DiscordActivity? activity, CancellationToken cancellationToken = default)
        {
            if (BreakNextSend)
            {
                BreakNextSend = false;
                IsConnected = false;
                throw new IOException("The pipe is broken.");
            }

            Sent.Add(activity);
            return Task.CompletedTask;
        }

        public void Disconnect() => IsConnected = false;

        /// <summary>Discord closed the pipe (the read loop noticed).</summary>
        public void Close() => IsConnected = false;

        public void Dispose() => IsConnected = false;
    }
}
