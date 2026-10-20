namespace Groovefin.Core.Playback;

/// <summary>A button of the taskbar thumbnail toolbar.</summary>
public enum TaskbarButton
{
    Previous,
    PlayPause,
    Next,
}

/// <summary>How the taskbar button shows progress.</summary>
public enum TaskbarProgressState
{
    /// <summary>No progress shown.</summary>
    None,

    /// <summary>Green bar: playing.</summary>
    Normal,

    /// <summary>Yellow bar: paused.</summary>
    Paused,

    /// <summary>Moving bar: opening or buffering.</summary>
    Indeterminate,
}

/// <summary>
/// The app's taskbar button: the previous / play-pause / next buttons in its thumbnail and the playback
/// progress on the button. Calls come from and events are raised on the UI thread.
/// </summary>
public interface ITaskbar
{
    /// <summary>Raised when a thumbnail toolbar button is clicked.</summary>
    event EventHandler<TaskbarButton>? ButtonPressed;

    /// <summary>Shows the buttons: enabled when there is a track; play or pause by <paramref name="isPlaying"/>.</summary>
    void UpdateButtons(bool hasTrack, bool isPlaying);

    /// <summary>Shows progress; <paramref name="fraction"/> (0–1) is used for <see cref="TaskbarProgressState.Normal"/> and <see cref="TaskbarProgressState.Paused"/>.</summary>
    void SetProgress(TaskbarProgressState state, double fraction);
}
