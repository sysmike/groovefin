using Groovefin.Audio;

namespace Groovefin.Core.Equalizer;

/// <summary>The equalizer in the playback engine's audio path.</summary>
public interface IAudioEqualizer
{
    /// <summary>Gets a value indicating whether the engine could add the equalizer; if not, Settings hides it.</summary>
    bool IsAvailable { get; }

    /// <summary>Applies new parameters to the playing audio and everything played after it.</summary>
    void Apply(EqualizerParameters parameters);
}
