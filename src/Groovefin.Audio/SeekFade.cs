namespace Groovefin.Audio;

/// <summary>
/// Brings the sound back softly after a jump in the stream (#283). After a seek far ahead, playback stops
/// until the new part has arrived and then starts abruptly at full level, and the decoder's first frame
/// there can be broken (an MP3 frame needs the data before it): both pop. <see cref="Start"/> keeps the next
/// <see cref="MuteTime"/> silent, which covers a whole MP3 or AAC frame, and then raises the level over
/// <see cref="FadeTime"/> along a raised cosine. Interleaved 32-bit float samples are changed in place.
/// Not thread-safe: use it from the thread that processes.
/// </summary>
public sealed class SeekFade
{
    /// <summary>How long it stays silent after the jump.</summary>
    public static readonly TimeSpan MuteTime = TimeSpan.FromMilliseconds(40);

    /// <summary>How long the level then takes to rise to full.</summary>
    public static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(20);

    private readonly int _channels;
    private readonly int _muteFrames;
    private readonly int _fadeFrames;

    /// <summary>The frame of the fade the next sample belongs to, or -1 when no fade runs.</summary>
    private int _frame = -1;

    public SeekFade(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _channels = channels;
        _muteFrames = (int)Math.Round(sampleRate * MuteTime.TotalSeconds);
        _fadeFrames = Math.Max(1, (int)Math.Round(sampleRate * FadeTime.TotalSeconds));
    }

    /// <summary>Gets a value indicating whether <see cref="Process"/> changes the samples.</summary>
    public bool IsActive => _frame >= 0;

    /// <summary>Starts again from silence: the stream jumped.</summary>
    public void Start() => _frame = 0;

    /// <summary>Applies the fade to interleaved samples in place; untouched when none runs. A partial frame at the end is left as it is.</summary>
    public void Process(Span<float> samples)
    {
        if (!IsActive)
        {
            return;
        }

        var frames = samples.Length / _channels;
        for (var frame = 0; frame < frames && IsActive; frame++)
        {
            var gain = Gain(_frame);
            var start = frame * _channels;
            for (var i = start; i < start + _channels; i++)
            {
                samples[i] *= gain;
            }

            _frame++;
            if (_frame >= _muteFrames + _fadeFrames)
            {
                _frame = -1;
            }
        }
    }

    private float Gain(int frame)
    {
        if (frame < _muteFrames)
        {
            return 0;
        }

        var progress = (frame - _muteFrames + 1) / (double)_fadeFrames;
        return (float)(0.5 - (0.5 * Math.Cos(Math.PI * Math.Min(progress, 1))));
    }
}
