using Groovefin.Core.Discord;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Discord;

public sealed class DiscordPresenceTests : IDisposable
{
    private static readonly DiscordApplication App = new("1234");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePlaybackEngine _engine = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly FakeDiscordConnection _discord = new();
    private DiscordPresence? _sut;

    public void Dispose() => _sut?.Dispose();

    private DiscordPresence Start(DiscordApplication? application = null) =>
        _sut = new DiscordPresence(_engine, _settings, _discord, application ?? App, _time, NullLogger<DiscordPresence>.Instance);

    private void TurnOn(bool on = true) => _settings.Update(s => s with { Playback = s.Playback with { DiscordPresence = on } });

    private static PlaybackSource Source(string name = "One More Time") => new("q1", new Uri("https://server/Audio/1"), new Track
    {
        Id = "t1",
        Name = name,
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
    public void Paused_it_shows_the_track_without_progress()
    {
        TurnOn();
        Start();
        Play();

        _engine.SimulateState(PlaybackState.Paused);

        _discord.Sent[^1]!.Start.Should().BeNull();
        _discord.Sent[^1]!.Details.Should().Be("One More Time");
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
    public void Stopping_clears_the_status_and_lets_go_of_Discord()
    {
        TurnOn();
        Start();
        Play();

        _engine.Stop();

        _discord.Sent[^1].Should().BeNull();
        _discord.IsConnected.Should().BeFalse();
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

        public void Dispose() => IsConnected = false;
    }
}
