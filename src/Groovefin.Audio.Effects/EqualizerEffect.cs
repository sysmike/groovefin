using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using WinRT;

// The list of formats crosses to Windows as IVectorView: its vtable must be generated for NativeAOT (#167).
[assembly: GeneratedWinRTExposedExternalType(typeof(List<AudioEncodingProperties>))]

namespace Groovefin.Audio.Effects;

/// <summary>
/// The graphic equalizer in MediaPlayer's audio path (<c>MediaPlayer.AddAudioEffect</c>). The app passes
/// <see cref="EqualizerParameters"/> through the property set, which the effect reads every
/// <see cref="ReadInterval"/> buffers (polling, not its MapChanged event: a generic WinRT delegate is one more
/// type to marshal under NativeAOT).
/// Buffers are processed in place as 32-bit float. While the equalizer is off or flat the samples pass
/// through untouched, and an error never stops playback: the buffer is left as it is.
/// </summary>
public sealed partial class EqualizerEffect : IBasicAudioEffect
{
    /// <summary>IID of the COM interface <c>IMemoryBufferByteAccess</c>, which gives the buffer's address.</summary>
    private static readonly Guid MemoryBufferByteAccess = new("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");

    /// <summary>How many buffers apart the property set is read: about a tenth of a second at 10 ms per buffer.</summary>
    private const int ReadInterval = 10;

    private readonly Lock _gate = new();
    private IPropertySet? _configuration;
    private EqualizerParameters _parameters = EqualizerParameters.Off;
    private EqualizerProcessor? _processor;
    private string? _parametersText;
    private int _sinceRead;
    private bool _changed;
    private bool _discard;

    /// <summary>Gets the formats it processes: float, the usual rates, stereo then mono. Media Foundation converts to one of them.</summary>
    public IReadOnlyList<AudioEncodingProperties> SupportedEncodingProperties { get; } = CreateEncodings();

    public bool TimeIndependent => true;

    public bool UseInputFrameForOutput => true;

    public void SetEncodingProperties(AudioEncodingProperties encodingProperties)
    {
        ArgumentNullException.ThrowIfNull(encodingProperties);
        lock (_gate)
        {
            _processor = new EqualizerProcessor((int)encodingProperties.SampleRate, (int)encodingProperties.ChannelCount);
            _changed = true;
        }
    }

    public void SetProperties(IPropertySet configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        Read(configuration);
    }

    public void ProcessFrame(ProcessAudioFrameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (++_sinceRead >= ReadInterval && _configuration is { } configuration)
        {
            _sinceRead = 0;
            Read(configuration);
        }

        EqualizerProcessor? processor;
        lock (_gate)
        {
            processor = _processor;
            if (processor is not null && _discard)
            {
                processor.Reset();
                _discard = false;
            }

            if (processor is not null && _changed)
            {
                processor.Configure(_parameters);
                _changed = false;
            }
        }

        if (processor is null || !processor.IsActive)
        {
            return;
        }

        try
        {
            using var buffer = context.InputFrame.LockBuffer(AudioBufferAccessMode.ReadWrite);
            using var reference = buffer.CreateReference();
            processor.Process(Samples(reference, buffer.Length));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException or NotSupportedException or InvalidCastException)
        {
            // The buffer stays as it is: better unequalized than silent.
        }
    }

    public void DiscardQueuedFrames()
    {
        lock (_gate)
        {
            _discard = true;
        }
    }

    public void Close(MediaEffectClosedReason reason) => _configuration = null;

    private static List<AudioEncodingProperties> CreateEncodings()
    {
        var encodings = new List<AudioEncodingProperties>();
        foreach (var channels in new uint[] { 2, 1 })
        {
            foreach (var rate in new uint[] { 48000, 44100, 96000, 88200, 192000, 176400, 32000 })
            {
                var encoding = AudioEncodingProperties.CreatePcm(rate, channels, 32);
                encoding.Subtype = MediaEncodingSubtypes.Float;
                encodings.Add(encoding);
            }
        }

        return encodings;
    }

    /// <summary>The samples of a locked buffer, through <c>IMemoryBufferByteAccess</c> (no built-in COM in NativeAOT).</summary>
    private static unsafe Span<float> Samples(IMemoryBufferReference reference, uint length)
    {
        var unknown = MarshalInspectable<object>.FromManaged(reference);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in MemoryBufferByteAccess, out var access));
            try
            {
                byte* data;
                uint capacity;
                var getBuffer = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, int>)(*(void***)access)[3];
                Marshal.ThrowExceptionForHR(getBuffer(access, &data, &capacity));

                // The reference keeps the memory alive while the span is used.
                return new Span<float>(data, (int)(Math.Min(length, capacity) / sizeof(float)));
            }
            finally
            {
                Marshal.Release(access);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>Takes the parameters from the property set when its text changed. Never throws: a bad read keeps the last parameters.</summary>
    private void Read(IPropertySet configuration)
    {
        string? text;
        try
        {
            text = configuration.TryGetValue(EqualizerEffectContract.ParametersKey, out var value) ? value as string : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
        {
            return;
        }

        if (text is null || string.Equals(text, _parametersText, StringComparison.Ordinal))
        {
            return;
        }

        _parametersText = text;
        if (EqualizerParameters.TryParse(text, out var parameters))
        {
            lock (_gate)
            {
                _parameters = parameters;
                _changed = true;
            }
        }
    }
}
