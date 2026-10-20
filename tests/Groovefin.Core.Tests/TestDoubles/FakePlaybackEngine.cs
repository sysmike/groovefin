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

    /// <summary>Moves on to the queued <see cref="IPlaybackEngine.Next"/>, like a gapless transition.</summary>
    public void SimulateAdvance() => ReportAdvanced(Next!);

    public void SimulateAdvance(PlaybackSource source) => ReportAdvanced(source);

    public void SimulateNextFailed() => ReportNextFailed(Next!);

    public void SimulateFailure(string message = "boom", Exception? exception = null) => ReportFailed(Current!, message, exception);

    public void SimulateFailure(PlaybackSource source) => ReportFailed(source, "boom", null);

    protected override void LoadCore(PlaybackSource source, bool autoPlay) => Calls.Add($"load {source.Id} autoPlay={autoPlay}");

    /// <summary>Gets how often the queued next source was set or cleared (not recorded in <see cref="Calls"/>).</summary>
    public int SetNextCount { get; private set; }

    protected override void SetNextCore(PlaybackSource? next) => SetNextCount++;

    protected override void PlayCore() => Calls.Add("play");

    protected override void PauseCore() => Calls.Add("pause");

    protected override void SeekCore(TimeSpan position) => Calls.Add($"seek {position.TotalSeconds}");

    /// <summary>Gets or sets whether the stream can be seeked, like a file played as it is (or not, like a transcode).</summary>
    public bool CanSeek { get; set; } = true;

    protected override bool CanSeekCore(TimeSpan position) => CanSeek;

    protected override void RestartCore(Uri url) => Calls.Add($"restart {url}");

    protected override void StopCore() => Calls.Add("stop");

    protected override void SetVolumeCore(double volume) => Calls.Add($"volume {volume}");

    protected override void SetMutedCore(bool isMuted) => Calls.Add($"muted {isMuted}");

    /// <summary>Gets or sets whether the fake can crossfade, like the Windows engine with its second player.</summary>
    public bool CanCrossfade { get; set; } = true;

    protected override bool SupportsCrossfade => CanCrossfade;

    protected override void CrossfadeCore(PlaybackSource next) => Calls.Add($"crossfade {next.Id}");

    protected override void SetFadingVolumeCore(double volume) => Calls.Add($"fading volume {volume:0.###}");

    protected override void EndFadeCore() => Calls.Add("end fade");
}
