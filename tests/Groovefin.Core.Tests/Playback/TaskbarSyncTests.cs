using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Playback;

public sealed class TaskbarSyncTests : IDisposable
{
    private static readonly Track Song = new() { Id = "t1", Name = "Digital Love", Duration = TimeSpan.FromSeconds(200) };

    private readonly FakeTimeProvider _time = new();
    private readonly FakePlaybackEngine _engine;
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly ITaskbar _taskbar = Substitute.For<ITaskbar>();
    private readonly InMemorySettingsService _settings = new();
    private readonly TaskbarSync _sut;

    public TaskbarSyncTests()
    {
        _engine = new FakePlaybackEngine(time: _time);
        _player.Engine.Returns(_engine);
        _player.Queue.Returns(new QueueManager());
        _sut = new TaskbarSync(_player, _taskbar, _settings);
    }

    public void Dispose() => _sut.Dispose();

    private void Load() => _engine.Load(new PlaybackSource("q1", new Uri("http://stream/t1"), Song));

    private void At(double seconds)
    {
        _time.Advance(TimeSpan.FromSeconds(1));
        _engine.SimulatePosition(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(200));
    }

    [Fact]
    public void Without_a_track_the_buttons_are_off_and_there_is_no_progress()
    {
        _taskbar.Received(1).UpdateButtons(false, false);
        _taskbar.Received(1).SetProgress(TaskbarProgressState.None, 0);
    }

    [Fact]
    public void Playing_shows_the_pause_button_and_green_progress()
    {
        Load();
        _engine.SimulateState(PlaybackState.Playing);
        At(50);

        _taskbar.Received().UpdateButtons(true, true);
        _taskbar.Received().SetProgress(TaskbarProgressState.Normal, 0.25);
    }

    [Fact]
    public void Paused_shows_yellow_progress_and_buffering_a_moving_bar()
    {
        Load();
        _engine.SimulateState(PlaybackState.Buffering);
        _taskbar.Received().SetProgress(TaskbarProgressState.Indeterminate, 0);

        _engine.SimulateState(PlaybackState.Playing);
        At(100);
        _engine.SimulateState(PlaybackState.Paused);

        _taskbar.Received().SetProgress(TaskbarProgressState.Paused, 0.5);
        _taskbar.Received().UpdateButtons(true, false);
    }

    [Fact]
    public void Small_position_changes_are_not_passed_on()
    {
        Load();
        _engine.SimulateState(PlaybackState.Playing);
        At(100);
        _taskbar.ClearReceivedCalls();

        At(100.1);
        _taskbar.DidNotReceiveWithAnyArgs().SetProgress(default, default);

        At(101);
        _taskbar.Received(1).SetProgress(TaskbarProgressState.Normal, 0.505);
    }

    [Fact]
    public void Turning_the_setting_off_hides_the_progress_but_keeps_the_buttons()
    {
        Load();
        _engine.SimulateState(PlaybackState.Playing);
        At(50);

        _settings.Update(s => s with { Appearance = s.Appearance with { ShowTaskbarProgress = false } });
        At(60);

        _taskbar.Received().SetProgress(TaskbarProgressState.None, 0);
        _taskbar.DidNotReceive().SetProgress(TaskbarProgressState.Normal, 0.3);
    }

    [Theory]
    [InlineData(TaskbarButton.Previous)]
    [InlineData(TaskbarButton.PlayPause)]
    [InlineData(TaskbarButton.Next)]
    public void Thumbnail_buttons_control_the_player(TaskbarButton button)
    {
        _taskbar.ButtonPressed += Raise.Event<EventHandler<TaskbarButton>>(_taskbar, button);

        switch (button)
        {
            case TaskbarButton.Previous:
                _player.Received(1).Previous();
                break;
            case TaskbarButton.PlayPause:
                _player.Received(1).TogglePlayPause();
                break;
            default:
                _player.Received(1).Next();
                break;
        }
    }
}
