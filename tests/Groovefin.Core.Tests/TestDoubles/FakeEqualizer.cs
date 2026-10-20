using Groovefin.Audio;
using Groovefin.Core.Equalizer;

namespace Groovefin.Core.Tests.TestDoubles;

/// <summary>Records what the app passes to the audio effect.</summary>
internal sealed class FakeEqualizer : IAudioEqualizer
{
    public bool IsAvailable { get; init; } = true;

    public List<EqualizerParameters> Applied { get; } = [];

    public void Apply(EqualizerParameters parameters) => Applied.Add(parameters);
}
