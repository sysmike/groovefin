using Groovefin.Core.Models;
using Groovefin.Core.Playback;

namespace Groovefin.Core.Tests.TestDoubles;

/// <summary>A <see cref="CurrentTrack"/> on a fake engine, for view model tests: <see cref="Play"/> loads a track.</summary>
internal sealed class PlayingTrack
{
    public PlayingTrack() => Current = new CurrentTrack(Engine);

    public FakePlaybackEngine Engine { get; } = new();

    public CurrentTrack Current { get; }

    public void Play(string trackId) =>
        Engine.Load(new PlaybackSource($"q-{trackId}", new Uri($"http://stream/{trackId}"), new Track { Id = trackId, Name = trackId }));
}
