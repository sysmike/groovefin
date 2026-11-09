using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Playback;

public sealed class SleepTimerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakePlaybackEngine _engine;
    private readonly QueueManager _queue = new(new Random(1));
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly SleepTimer _sut;

    public SleepTimerTests()
    {
        _engine = new FakePlaybackEngine(time: _time);
        _player.Engine.Returns(_engine);
        _player.Queue.Returns(_queue);
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _sut = new SleepTimer(_player, _time, _strings);
        _engine.Volume = 0.8;
    }

    public void Dispose() => _sut.Dispose();

    private static Track Song(string id, string album) => new() { Id = id, Name = id, AlbumId = album, Duration = TimeSpan.FromMinutes(4) };

    /// <summary>Plays the first of <paramref name="songs"/> at <paramref name="position"/> of its four minutes.</summary>
    private void Playing(TimeSpan position, params Track[] songs)
    {
        _queue.PlayNow(songs);
        _engine.Load(new PlaybackSource(_queue.Current!.Id, new Uri("http://stream/a"), songs[0]));
        _engine.SimulateState(PlaybackState.Playing);
        _engine.SimulatePosition(position, TimeSpan.FromMinutes(4));
        _engine.Calls.Clear();
    }

    [Fact]
    public void Minutes_fade_out_then_pause_and_put_the_volume_back()
    {
        Playing(TimeSpan.Zero, Song("a", "x"));

        _sut.StartMinutesCommand.Execute(15);
        _sut.Description.Should().Be("SleepTimer_PausesIn");
        _time.Advance(TimeSpan.FromMinutes(15) - SleepTimer.FadeDuration);
        _engine.Calls.Should().NotContain("pause");

        _time.Advance(SleepTimer.FadeDuration / 2);
        _engine.Volume.Should().BeLessThan(0.8).And.BeGreaterThan(0);

        _time.Advance(SleepTimer.FadeDuration);
        _engine.Calls.Should().Contain("pause");
        _engine.Volume.Should().Be(0.8, "the next Play is as loud as before");
        _sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Minutes_left_count_down()
    {
        _sut.StartMinutesCommand.Execute(30);
        _sut.MinutesLeft.Should().Be(30);

        _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        _sut.MinutesLeft.Should().Be(20);
    }

    [Fact]
    public void Music_already_paused_is_left_alone_and_the_timer_ends()
    {
        Playing(TimeSpan.Zero, Song("a", "x"));
        _engine.SimulateState(PlaybackState.Paused);

        _sut.StartMinutesCommand.Execute(15);
        _time.Advance(TimeSpan.FromMinutes(16));

        _engine.Calls.Should().NotContain("pause");
        _sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void The_end_of_a_song_fades_out_before_the_next_starts()
    {
        Playing(TimeSpan.FromMinutes(3), Song("a", "x"), Song("b", "x"));
        _sut.StartEndOfTrackCommand.Execute(null);
        _sut.Description.Should().Be("SleepTimer_PausesAfterTrack");

        _time.Advance(TimeSpan.FromSeconds(1));
        _engine.SimulatePosition(TimeSpan.FromMinutes(4) - SleepTimer.FadeDuration, TimeSpan.FromMinutes(4));
        _time.Advance(SleepTimer.FadeDuration);

        _engine.Calls.Should().Contain("pause");
        _sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void The_end_of_an_album_waits_for_its_last_song()
    {
        Playing(TimeSpan.FromMinutes(3), Song("a", "x"), Song("b", "x"), Song("c", "y"));
        _sut.StartEndOfAlbumCommand.Execute(null);

        _time.Advance(TimeSpan.FromSeconds(1));
        _engine.SimulatePosition(TimeSpan.FromMinutes(4) - TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(4));
        _time.Advance(SleepTimer.FadeDuration);
        _engine.Calls.Should().NotContain("pause", "the album's next song follows");

        _queue.Next();
        _engine.SimulatePosition(TimeSpan.FromMinutes(4) - TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(4));
        _time.Advance(SleepTimer.FadeDuration);
        _engine.Calls.Should().Contain("pause", "the queue goes on with another album");
    }

    [Fact]
    public void Turning_it_off_during_the_fade_puts_the_volume_back()
    {
        Playing(TimeSpan.Zero, Song("a", "x"));
        _sut.StartMinutesCommand.Execute(15);
        _time.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(2));

        _sut.CancelCommand.Execute(null);
        _time.Advance(TimeSpan.FromMinutes(1));

        _engine.Volume.Should().Be(0.8);
        _engine.Calls.Should().NotContain("pause");
        _sut.Description.Should().Be("SleepTimer_Off");
    }
}
