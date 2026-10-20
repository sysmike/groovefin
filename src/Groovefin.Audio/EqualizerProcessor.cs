namespace Groovefin.Audio;

/// <summary>
/// The graphic equalizer for one stream: the preamp and a <see cref="PeakingFilter"/> per band, applied
/// in place to interleaved 32-bit float samples. It calculates in double precision and keeps the filter
/// state between buffers. Bands at 0 dB are skipped; while nothing is active the samples stay untouched.
/// Not thread-safe: configure it from the thread that processes.
/// </summary>
public sealed class EqualizerProcessor
{
    /// <summary>Filter state below this is set to 0, so silence does not decay into slow denormal numbers.</summary>
    private const double Tiny = 1e-20;

    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly PeakingFilter[] _filters;
    private readonly double[] _state;
    private int[] _active = [];
    private bool _amplify;
    private float _preamp = 1;

    /// <summary>Initializes a new instance for audio with <paramref name="channels"/> interleaved channels.</summary>
    public EqualizerProcessor(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _sampleRate = sampleRate;
        _channels = channels;
        _filters = [.. Enumerable.Repeat(PeakingFilter.Identity, EqualizerBands.Count)];
        _state = new double[EqualizerBands.Count * channels * 4];
    }

    /// <summary>Gets a value indicating whether <see cref="Process"/> changes the samples.</summary>
    public bool IsActive => _active.Length > 0 || _amplify;

    /// <summary>Applies new parameters. Bands that stay active keep their state, so playback continues without a click.</summary>
    public void Configure(EqualizerParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var active = new List<int>();
        for (var band = 0; band < EqualizerBands.Count; band++)
        {
            var filter = parameters.Enabled
                ? PeakingFilter.Create(EqualizerBands.Frequencies[band], parameters.Gains[band], EqualizerBands.Q, _sampleRate)
                : PeakingFilter.Identity;
            if (filter.IsIdentity)
            {
                // Starts from silence when it is turned on again.
                BandState(band).Clear();
            }
            else
            {
                active.Add(band);
            }

            _filters[band] = filter;
        }

        _active = [.. active];
        _amplify = parameters.Enabled && !EqualizerBands.IsFlat(parameters.Preamp);
        _preamp = _amplify ? (float)Math.Pow(10, parameters.Preamp / 20) : 1;
    }

    /// <summary>Forgets the filter state, e.g. after a seek.</summary>
    public void Reset() => Array.Clear(_state);

    /// <summary>Equalizes interleaved samples in place. A partial frame at the end is left as it is.</summary>
    public void Process(Span<float> samples)
    {
        if (!IsActive)
        {
            return;
        }

        var frames = samples.Length / _channels;
        foreach (var band in _active)
        {
            for (var channel = 0; channel < _channels; channel++)
            {
                ProcessChannel(samples, frames, channel, band);
            }
        }

        if (_amplify)
        {
            var end = frames * _channels;
            for (var i = 0; i < end; i++)
            {
                samples[i] *= _preamp;
            }
        }
    }

    /// <summary>Direct form I: x1, x2, y1, y2 per filter and channel.</summary>
    private void ProcessChannel(Span<float> samples, int frames, int channel, int band)
    {
        var filter = _filters[band];
        var state = BandState(band).Slice(channel * 4, 4);
        double x1 = state[0], x2 = state[1], y1 = state[2], y2 = state[3];
        for (var frame = 0; frame < frames; frame++)
        {
            var i = (frame * _channels) + channel;
            double x = samples[i];
            var y = (filter.B0 * x) + (filter.B1 * x1) + (filter.B2 * x2) - (filter.A1 * y1) - (filter.A2 * y2);
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            samples[i] = (float)y;
        }

        state[0] = Flush(x1);
        state[1] = Flush(x2);
        state[2] = Flush(y1);
        state[3] = Flush(y2);
    }

    private Span<double> BandState(int band) => _state.AsSpan(band * _channels * 4, _channels * 4);

    private static double Flush(double value) => Math.Abs(value) < Tiny ? 0 : value;
}
