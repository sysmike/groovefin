namespace Groovefin.Core.Playback;

/// <summary>A button of the system media controls: media keys, the media flyout, Bluetooth headsets.</summary>
public enum MediaControlsButton
{
    Play,
    Pause,
    Next,
    Previous,
}

/// <summary>Which button of the system media controls was pressed.</summary>
public sealed class MediaControlsButtonEventArgs(MediaControlsButton button) : EventArgs
{
    public MediaControlsButton Button { get; } = button;
}

/// <summary>What the system media controls show about the playing track.</summary>
/// <param name="Title">The track's name.</param>
/// <param name="Artist">The track's artists, comma separated; empty when unknown.</param>
/// <param name="Album">The album's name; empty when unknown.</param>
/// <param name="ArtworkPath">A local file with the album cover, or <see langword="null"/> for none.</param>
public sealed record MediaControlsDisplay(string Title, string Artist, string Album, string? ArtworkPath);

/// <summary>
/// The operating system's media controls (on Windows the System Media Transport Controls). Implemented
/// per platform; <see cref="SystemMediaControlsSync"/> keeps them in line with the player.
/// </summary>
public interface ISystemMediaControls
{
    /// <summary>Raised when the user presses a button. May be raised on any thread.</summary>
    event EventHandler<MediaControlsButtonEventArgs>? ButtonPressed;

    /// <summary>Shows whether the player is playing, paused, loading or idle.</summary>
    void SetStatus(PlaybackState state);

    /// <summary>Shows the track's position and length.</summary>
    void SetTimeline(TimeSpan position, TimeSpan duration);

    /// <summary>
    /// Shows <paramref name="display"/>, or clears the controls when it is <see langword="null"/>. When
    /// calls overlap, the last one wins. Artwork that cannot be loaded is left out.
    /// </summary>
    Task SetDisplayAsync(MediaControlsDisplay? display);
}
