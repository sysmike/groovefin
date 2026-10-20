namespace Groovefin.Audio;

/// <summary>What the app and the audio effect (Groovefin.Audio.Effects) agree on.</summary>
public static class EqualizerEffectContract
{
    /// <summary>The WinRT class MediaPlayer creates; the manifests register it in <c>Groovefin.Audio.Effects.dll</c>.</summary>
    public const string ClassId = "Groovefin.Audio.Effects.EqualizerEffect";

    /// <summary>The key in the effect's property set: <see cref="EqualizerParameters.Format"/> text.</summary>
    public const string ParametersKey = "Groovefin.Equalizer";
}
