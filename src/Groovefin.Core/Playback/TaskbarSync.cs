using Groovefin.Core.Settings;

namespace Groovefin.Core.Playback;

/// <summary>
/// Keeps the taskbar button in line with the player: the thumbnail buttons follow the track and the
/// playback state, the progress bar follows the position (when <see cref="AppearanceSettings.ShowTaskbarProgress"/>
/// is on) in steps of <see cref="ProgressStep"/>. Thumbnail button clicks control the player. Create it once
/// after the main window is shown and keep it alive.
/// </summary>
public sealed class TaskbarSync : IDisposable
{
    /// <summary>The smallest progress change passed on: a thousandth of the track.</summary>
    public const double ProgressStep = 0.001;

    private readonly IPlayer _player;
    private readonly IPlaybackEngine _engine;
    private readonly ITaskbar _taskbar;
    private readonly ISettingsService _settings;
    private (TaskbarProgressState State, double Fraction)? _shown;
    private (bool HasTrack, bool IsPlaying)? _buttons;

    public TaskbarSync(IPlayer player, ITaskbar taskbar, ISettingsService settings)
    {
        _player = player;
        _engine = player.Engine;
        _taskbar = taskbar;
        _settings = settings;

        _engine.CurrentChanged += OnPlaybackChanged;
        _engine.StateChanged += OnPlaybackChanged;
        _engine.PositionChanged += OnPlaybackChanged;
        _settings.Changed += OnSettingsChanged;
        _taskbar.ButtonPressed += OnButtonPressed;
        Update();
    }

    public void Dispose()
    {
        _engine.CurrentChanged -= OnPlaybackChanged;
        _engine.StateChanged -= OnPlaybackChanged;
        _engine.PositionChanged -= OnPlaybackChanged;
        _settings.Changed -= OnSettingsChanged;
        _taskbar.ButtonPressed -= OnButtonPressed;
    }

    private bool IsPlaying => _engine.State is PlaybackState.Playing or PlaybackState.Buffering or PlaybackState.Opening;

    private void OnPlaybackChanged(object? sender, EventArgs e) => Update();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Appearance.ShowTaskbarProgress != e.NewSettings.Appearance.ShowTaskbarProgress)
        {
            Update();
        }
    }

    private void OnButtonPressed(object? sender, TaskbarButton button)
    {
        switch (button)
        {
            case TaskbarButton.Previous:
                _player.Previous();
                break;
            case TaskbarButton.PlayPause:
                _player.TogglePlayPause();
                break;
            case TaskbarButton.Next:
                _player.Next();
                break;
        }
    }

    /// <summary>Passes on what changed: the buttons, and the progress when its state or step changed.</summary>
    private void Update()
    {
        var buttons = (_engine.Current is not null, IsPlaying);
        if (buttons != _buttons)
        {
            _buttons = buttons;
            _taskbar.UpdateButtons(buttons.Item1, buttons.Item2);
        }

        var progress = Progress();
        if (_shown is not { } shown || shown.State != progress.State || Math.Abs(shown.Fraction - progress.Fraction) >= ProgressStep)
        {
            _shown = progress;
            _taskbar.SetProgress(progress.State, progress.Fraction);
        }
    }

    private (TaskbarProgressState State, double Fraction) Progress()
    {
        if (!_settings.Current.Appearance.ShowTaskbarProgress || _engine.Current is null)
        {
            return (TaskbarProgressState.None, 0);
        }

        var state = _engine.State switch
        {
            PlaybackState.Opening or PlaybackState.Buffering => TaskbarProgressState.Indeterminate,
            PlaybackState.Paused => TaskbarProgressState.Paused,
            PlaybackState.Playing => TaskbarProgressState.Normal,
            _ => TaskbarProgressState.None,
        };
        var duration = _engine.Duration.TotalSeconds;
        var fraction = duration > 0 ? Math.Clamp(_engine.Position.TotalSeconds / duration, 0, 1) : 0;
        return state is TaskbarProgressState.Normal or TaskbarProgressState.Paused ? (state, fraction) : (state, 0);
    }
}
