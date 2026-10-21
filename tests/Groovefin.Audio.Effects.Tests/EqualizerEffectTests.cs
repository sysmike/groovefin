using System.Buffers.Binary;
using Groovefin.Playback.Windows;
using Windows.Foundation.Collections;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Groovefin.Audio.Effects.Tests;

/// <summary>
/// Sends a tone through the published equalizer effect with Media Foundation's transcoder. That creates the
/// effect by its class name and feeds it like MediaPlayer does, but needs no sound card, which the CI runners
/// do not have (#169). The parameters go through the DLL's exports, as in the app.
/// </summary>
public sealed class EqualizerEffectTests : IDisposable
{
    private const int SampleRate = 44100;
    private const int Channels = 2;
    private const double ToneFrequency = 1000;
    private const double ToneAmplitude = 0.0625;
    private static readonly TimeSpan ToneLength = TimeSpan.FromSeconds(2);

    /// <summary>The band whose centre is the tone's frequency (1 kHz).</summary>
    private static readonly int ToneBand = Array.IndexOf(EqualizerBands.Frequencies.ToArray(), ToneFrequency);

    private readonly string _folder = Directory.CreateTempSubdirectory("groovefin-eq-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file Media Foundation still holds stays in the temp folder.
        }
    }

    [Fact]
    public async Task Off_leaves_the_tone_as_it_is()
    {
        var gain = await MeasureGainAsync(EqualizerParameters.Off);

        gain.Should().BeApproximately(0, 0.5);
    }

    [Fact]
    public async Task Boosting_the_tones_band_makes_it_louder()
    {
        var before = EqualizerEffectLibrary.GetStatus();

        var gain = await MeasureGainAsync(WithBand(12));

        gain.Should().BeInRange(10, 13);
        var after = EqualizerEffectLibrary.GetStatus();
        after.ProcessedBuffers.Should().BeGreaterThan(before.ProcessedBuffers);
        after.Errors.Should().Be(before.Errors, $"the last error was 0x{after.LastError:X8}");
    }

    [Fact]
    public async Task Cutting_the_tones_band_makes_it_quieter()
    {
        var gain = await MeasureGainAsync(WithBand(-12));

        gain.Should().BeInRange(-13, -10);
    }

    [Fact]
    public async Task The_preamp_changes_the_level()
    {
        var gain = await MeasureGainAsync(new EqualizerParameters(true, -6, new double[EqualizerBands.Count]));

        gain.Should().BeApproximately(-6, 0.5);
    }

    private static EqualizerParameters WithBand(double gain)
    {
        var gains = new double[EqualizerBands.Count];
        gains[ToneBand] = gain;
        return new EqualizerParameters(true, 0, gains);
    }

    /// <summary>Transcodes the tone through the effect with <paramref name="parameters"/>; returns how much louder it came out, in dB.</summary>
    private async Task<double> MeasureGainAsync(EqualizerParameters parameters)
    {
        EqualizerEffectLibrary.SetParameters(parameters);
        var tone = Tone();
        var inputPath = Path.Join(_folder, $"{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(inputPath, Wav(tone));
        var input = await StorageFile.GetFileFromPathAsync(inputPath);
        var folder = await StorageFolder.GetFolderFromPathAsync(_folder);
        var output = await folder.CreateFileAsync("out.wav", CreationCollisionOption.GenerateUniqueName);

        var transcoder = new MediaTranscoder { AlwaysReencode = true };
        transcoder.AddAudioEffect(EqualizerEffectContract.ClassId, effectRequired: true, new PropertySet());
        var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
        profile.Audio = AudioEncodingProperties.CreatePcm(SampleRate, Channels, 16);
        var transcode = await transcoder.PrepareFileTranscodeAsync(input, output, profile);
        transcode.CanTranscode.Should().BeTrue($"Media Foundation could not transcode with the effect: {transcode.FailureReason}");
        await transcode.TranscodeAsync();

        var result = ReadWav(File.ReadAllBytes(output.Path));
        return 20 * Math.Log10(Rms(result) / Rms(tone));
    }

    /// <summary>The tone, interleaved stereo.</summary>
    private static float[] Tone()
    {
        var frames = (int)(SampleRate * ToneLength.TotalSeconds);
        var samples = new float[frames * Channels];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)(ToneAmplitude * Math.Sin(2 * Math.PI * ToneFrequency * i / SampleRate));
            for (var c = 0; c < Channels; c++)
            {
                samples[(i * Channels) + c] = value;
            }
        }

        return samples;
    }

    /// <summary>The RMS of the middle of a signal: the filters' and the codec's start and end are left out.</summary>
    private static double Rms(float[] samples)
    {
        var skip = SampleRate / 4 * Channels;
        var middle = samples.AsSpan(skip, samples.Length - (2 * skip));
        var sum = 0.0;
        foreach (var sample in middle)
        {
            sum += sample * (double)sample;
        }

        return Math.Sqrt(sum / middle.Length);
    }

    /// <summary>A 16-bit PCM WAV file of interleaved samples.</summary>
    private static byte[] Wav(float[] samples)
    {
        var data = samples.Length * 2;
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
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + (2 * i))..], (short)Math.Round(samples[i] * short.MaxValue));
        }

        return bytes;
    }

    /// <summary>The samples of a WAV file the transcoder wrote: 16-bit PCM or 32-bit float.</summary>
    private static float[] ReadWav(byte[] bytes)
    {
        var span = bytes.AsSpan();
        span[..4].SequenceEqual("RIFF"u8).Should().BeTrue("the transcoder writes a WAV file");
        var bits = 0;
        for (var offset = 12; offset + 8 <= span.Length;)
        {
            var id = span.Slice(offset, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]), (uint)(span.Length - offset - 8));
            var body = span.Slice(offset + 8, size);
            if (id.SequenceEqual("fmt "u8))
            {
                BinaryPrimitives.ReadInt16LittleEndian(body[2..]).Should().Be(Channels);
                bits = BinaryPrimitives.ReadInt16LittleEndian(body[14..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                var bytesPerSample = bits / 8;
                if (bits is not (16 or 32))
                {
                    throw new InvalidDataException($"Unexpected WAV format: {bits} bits");
                }

                var samples = new float[size / bytesPerSample];
                for (var i = 0; i < samples.Length; i++)
                {
                    var sample = body[(i * bytesPerSample)..];
                    samples[i] = bits == 16
                        ? BinaryPrimitives.ReadInt16LittleEndian(sample) / (float)short.MaxValue
                        : BinaryPrimitives.ReadSingleLittleEndian(sample);
                }

                return samples;
            }

            offset += 8 + size + (size & 1);
        }

        throw new InvalidDataException("The WAV file has no data chunk");
    }
}
