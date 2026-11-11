using System.Buffers.Binary;
using System.Collections.Concurrent;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Playback.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Audio.Effects.Tests;

/// <summary>
/// The engine on a real <c>MediaPlayer</c> (#437): what <see cref="PlaybackEngineBase"/>'s tests cannot show,
/// because they run on a fake. A WAV file of a quiet tone stands in for a stream. The CI runners have no sound
/// card: Media Foundation then fails at the audio output (0xC00D36FA) once it has built everything before it,
/// so a test that plays accepts either sound or exactly that failure, as the smoke test does.
/// </summary>
[Collection(MediaFoundationCollection.Name)]
public sealed class MediaPlayerEngineTests : IDisposable
{
    /// <summary>MF_E_NO_AUDIO_PLAYBACK_DEVICE: there is no audio output.</summary>
    private const int NoAudioOutput = unchecked((int)0xC00D36FA);

    private const int SampleRate = 44100;
    private const int Channels = 2;
    private static readonly TimeSpan ToneLength = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _folder = Directory.CreateTempSubdirectory("groovefin-engine-").FullName;
    private readonly UiThread _ui = new();
    private readonly MediaPlayerEngine _sut;
    private readonly ConcurrentQueue<PlaybackFailedEventArgs> _failures = new();
    private int _currentChanges;

    public MediaPlayerEngineTests()
    {
        _sut = On(() => new MediaPlayerEngine(_ui, TimeProvider.System, NullLogger<MediaPlayerEngine>.Instance));
        _sut.Failed += (_, e) => _failures.Enqueue(e);
        _sut.CurrentChanged += (_, _) => Interlocked.Increment(ref _currentChanges);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        On(_sut.Dispose);
        _ui.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file Media Foundation still holds stays in the temp folder.
        }
    }

    [Fact]
    public async Task A_file_plays_or_fails_only_at_the_audio_output()
    {
        var source = Tone("a");

        On(() => _sut.Load(source));

        On(() => _sut.Current).Should().Be(source);
        await UntilAsync(() => On(() => _sut.Position) >= TimeSpan.FromSeconds(1) || !_failures.IsEmpty);
        if (_failures.TryPeek(out var failure))
        {
            failure.Source.Should().Be(source);
            IsNoAudioOutput(failure).Should().BeTrue($"without a sound card that is the only failure there may be, not: {Describe(failure)}");
        }
        else
        {
            On(() => _sut.Position).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1), "the tone plays");
            On(() => _sut.State).Should().Be(PlaybackState.Playing);
            On(() => _sut.Duration).Should().BeCloseTo(ToneLength, TimeSpan.FromMilliseconds(500));
        }
    }

    [Fact]
    public async Task A_file_that_is_not_there_fails()
    {
        var source = new PlaybackSource("missing", new Uri(Path.Join(_folder, "missing.wav")), new Track { Id = "missing", Name = "Missing" });

        On(() => _sut.Load(source));
        await UntilAsync(() => !_failures.IsEmpty);

        _failures.Should().NotBeEmpty("a file that is not there cannot play");
        _failures.Should().OnlyContain(f => f.Source == source);
        _failures.Should().NotContain(f => IsNoAudioOutput(f), "the file fails before the audio output is needed");
    }

    [Fact]
    public void Stop_unloads_the_source()
    {
        On(() => _sut.Load(Tone("a"), autoPlay: false));

        On(_sut.Stop);

        On(() => _sut.Current).Should().BeNull();
        On(() => _sut.State).Should().Be(PlaybackState.Idle);
        On(() => _sut.Position).Should().Be(TimeSpan.Zero);
        Volatile.Read(ref _currentChanges).Should().Be(2, "once for the load, once for the stop");
    }

    [Fact]
    public void Loading_another_source_replaces_the_first_and_what_was_queued_after_it()
    {
        var first = Tone("a");
        var queued = Tone("b");
        var other = Tone("c");
        On(() => _sut.Load(first, autoPlay: false));
        On(() => _sut.SetNext(queued));
        On(() => _sut.Next).Should().Be(queued);

        On(() => _sut.Load(other, autoPlay: false));

        On(() => _sut.Current).Should().Be(other);
        On(() => _sut.Next).Should().BeNull();
    }

    [Fact]
    public void The_volume_stays_between_silent_and_full_and_mute_is_kept()
    {
        On(() => _sut.Load(Tone("a"), autoPlay: false));

        On(() => _sut.Volume = 2);
        On(() => _sut.Volume).Should().Be(1);
        On(() => _sut.Volume = -1);
        On(() => _sut.Volume).Should().Be(0);
        On(() => _sut.Volume = 0.25);
        On(() => _sut.IsMuted = true);

        On(() => _sut.Volume).Should().Be(0.25);
        On(() => _sut.IsMuted).Should().BeTrue();
    }

    [Fact]
    public void The_equalizer_effect_is_in_the_audio_path_and_takes_parameters()
    {
        // This process registers the effect as the app does (app.manifest), so Windows can create it.
        _sut.IsAvailable.Should().BeTrue();

        var gains = new double[EqualizerBands.Count];
        gains[0] = 6;
        var apply = () => On(() => _sut.Apply(new EqualizerParameters(true, -3, gains)));

        apply.Should().NotThrow();
        On(() => _sut.Apply(EqualizerParameters.Off));
    }

    [Fact]
    public async Task The_output_devices_can_be_listed_and_the_default_chosen()
    {
        // None on a CI runner; the list must come all the same.
        var devices = await _sut.GetDevicesAsync();

        devices.Should().NotBeNull();
        devices.Should().OnlyContain(d => !string.IsNullOrEmpty(d.Id));
        var useDefault = () => On(() => _sut.Use(null));
        useDefault.Should().NotThrow();
    }

    [Fact]
    public void Disposing_twice_is_safe()
    {
        On(() => _sut.Load(Tone("a"), autoPlay: false));

        // The container disposes the engine once for each of its registrations (#145).
        On(_sut.Dispose);
        var again = () => On(_sut.Dispose);

        again.Should().NotThrow();
    }

    private static bool IsNoAudioOutput(PlaybackFailedEventArgs failure) =>
        failure.Exception?.HResult == NoAudioOutput || failure.Message.Contains("C00D36FA", StringComparison.OrdinalIgnoreCase);

    private static string Describe(PlaybackFailedEventArgs failure) =>
        $"{failure.Message} (0x{failure.Exception?.HResult ?? 0:X8})";

    /// <summary>Runs <paramref name="action"/> on the test's UI thread and waits for it.</summary>
    private void On(Action action) => _ui.EnqueueAsync(action).GetAwaiter().GetResult();

    private T On<T>(Func<T> function) => _ui.EnqueueAsync(function).GetAwaiter().GetResult();

    private static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Patience;
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(100, Ct);
        }
    }

    /// <summary>A source that plays a WAV file of a quiet 440 Hz tone, <see cref="ToneLength"/> long.</summary>
    private PlaybackSource Tone(string id)
    {
        var path = Path.Join(_folder, id + ".wav");
        File.WriteAllBytes(path, Wav());
        return new PlaybackSource(id, new Uri(path), new Track { Id = id, Name = $"Tone {id}" });
    }

    /// <summary>A 16-bit PCM WAV file of the tone.</summary>
    private static byte[] Wav()
    {
        var frames = (int)(SampleRate * ToneLength.TotalSeconds);
        var data = frames * Channels * 2;
        var bytes = new byte[44 + data];
        var span = bytes.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + data);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * Channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], Channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], data);
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = (short)Math.Round(0.05 * Math.Sin(2 * Math.PI * 440 * frame / SampleRate) * short.MaxValue);
            for (var channel = 0; channel < Channels; channel++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(span[(44 + (((frame * Channels) + channel) * 2))..], sample);
            }
        }

        return bytes;
    }
}
