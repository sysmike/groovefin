using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.Tests.TestDoubles;

/// <summary>
/// <see cref="IPlaybackEngine"/> for ViewModel tests: records the calls and lets a test play the part of
/// the platform player (<c>Simulate…</c>).
/// </summary>
internal sealed class FakePlaybackEngine(IDispatcher? dispatcher = null, TimeProvider? time = null)
    : PlaybackEngineBase(dispatcher ?? new ImmediateDispatcher(), time ?? TimeProvider.System)
{
    public List<string> Calls { get; } = [];

    public void SimulateState(PlaybackState state) => ReportState(state);

    public void SimulatePosition(TimeSpan position, TimeSpan duration) => ReportPosition(position, duration);

    public void SimulateEnded() => ReportEnded(Current!);

    public void SimulateEnded(PlaybackSource source) => ReportEnded(source);

    public void SimulateFailure(string message = "boom", Exception? exception = null) => ReportFailed(Current!, message, exception);

    public void SimulateFailure(PlaybackSource source) => ReportFailed(source, "boom", null);

    protected override void LoadCore(PlaybackSource source, bool autoPlay) => Calls.Add($"load {source.Id} autoPlay={autoPlay}");

    protected override void PlayCore() => Calls.Add("play");

    protected override void PauseCore() => Calls.Add("pause");

    protected override void SeekCore(TimeSpan position) => Calls.Add($"seek {position.TotalSeconds}");

    protected override void StopCore() => Calls.Add("stop");

    protected override void SetVolumeCore(double volume) => Calls.Add($"volume {volume}");

    protected override void SetMutedCore(bool isMuted) => Calls.Add($"muted {isMuted}");
}
