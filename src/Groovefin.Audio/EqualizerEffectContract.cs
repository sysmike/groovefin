namespace Groovefin.Audio;

/// <summary>What the app and the audio effect (Groovefin.Audio.Effects) agree on.</summary>
/// <remarks>
/// The parameters do not travel in the property set given to <c>MediaPlayer.AddAudioEffect</c>: changes to it
/// did not reach the effect (#169). The DLL exports functions instead, and its effects all use what was
/// set last. MediaPlayer loads the DLL into the app's process, so the app calls the same DLL instance.
/// </remarks>
public static class EqualizerEffectContract
{
    /// <summary>The WinRT class MediaPlayer creates; the manifests register it in <see cref="LibraryName"/>.</summary>
    public const string ClassId = "Groovefin.Audio.Effects.EqualizerEffect";

    /// <summary>The effect's native DLL, next to the app's executable.</summary>
    public const string LibraryName = "Groovefin.Audio.Effects.dll";

    /// <summary>
    /// <c>int SetParameters(const wchar_t* text, int length)</c>: <see cref="EqualizerParameters.Format"/> text,
    /// used from the next buffer on. Returns 0, or E_INVALIDARG for text that does not parse.
    /// </summary>
    public const string SetParametersExport = "GroovefinEqualizerSetParameters";

    /// <summary><c>int GetStatus(EqualizerEffectStatus* status)</c>: the counters of all effects. Returns 0.</summary>
    public const string GetStatusExport = "GroovefinEqualizerGetStatus";

    /// <summary>
    /// <c>int FadeInNextStream()</c>: the next stream an effect starts on, within a few seconds, begins with a
    /// <see cref="SeekFade"/>. For a transcode restarted at a seek target (#283), which is a new stream and
    /// not a jump in the old one. Returns 0.
    /// </summary>
    public const string FadeInNextStreamExport = "GroovefinEqualizerFadeInNextStream";
}
