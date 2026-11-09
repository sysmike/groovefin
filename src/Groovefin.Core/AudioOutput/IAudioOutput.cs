namespace Groovefin.Core.AudioOutput;

/// <summary>A device Windows can play sound on (#389): its ID and its name, e.g. "Speakers (Realtek Audio)".</summary>
public sealed record AudioOutputDevice(string Id, string Name);

/// <summary>Where the music plays (#389): the Windows default device, or one the user chose.</summary>
public interface IAudioOutput
{
    /// <summary>Gets the devices that can play sound now.</summary>
    Task<IReadOnlyList<AudioOutputDevice>> GetDevicesAsync();

    /// <summary>
    /// Plays on the device with <paramref name="deviceId"/>, or on the Windows default for <see langword="null"/>.
    /// While that device is not connected the default plays, and the device takes over again once it is back.
    /// Playback goes on without a restart.
    /// </summary>
    void Use(string? deviceId);
}
