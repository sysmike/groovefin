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
/// The graphic equalizer in MediaPlayer's audio path (<c>MediaPlayer.AddAudioEffect</c>). It plays with the
/// parameters the app set last through the DLL's exports (<see cref="EqualizerEffectState"/>), which it checks
/// at every buffer; its property set stays unused (#169).
/// Buffers are processed in place as 32-bit float. While the equalizer is off or flat the samples pass
/// through untouched, and an error never stops playback: the buffer is left as it is, and the error counted.
/// </summary>
public sealed partial class EqualizerEffect : IBasicAudioEffect
{
    /// <summary>IID of the COM interface <c>IMemoryBufferByteAccess</c>, which gives the buffer's address.</summary>
    private static readonly Guid MemoryBufferByteAccess = new("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");

    private readonly Lock _gate = new();
    private EqualizerProcessor? _processor;
    private EqualizerParameters? _applied;
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
            _applied = null;
        }
    }

    /// <summary>Not used: the parameters come through the DLL's exports (#169).</summary>
    public void SetProperties(IPropertySet configuration)
    {
    }

    public void ProcessFrame(ProcessAudioFrameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EqualizerEffectState.CountBuffer();
        var parameters = EqualizerEffectState.Parameters;
        EqualizerProcessor? processor;
        lock (_gate)
        {
            processor = _processor;
            if (processor is null)
            {
                return;
            }

            if (_discard)
            {
                processor.Reset();
                _discard = false;
            }

            if (!ReferenceEquals(parameters, _applied))
            {
                processor.Configure(parameters);
                _applied = parameters;
            }
        }

        if (!processor.IsActive)
        {
            return;
        }

        try
        {
            using var buffer = context.InputFrame.LockBuffer(AudioBufferAccessMode.ReadWrite);
            using var reference = buffer.CreateReference();
            processor.Process(Samples(reference, buffer.Length));
            EqualizerEffectState.CountProcessed();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException or NotSupportedException or InvalidCastException)
        {
            // The buffer stays as it is: better unequalized than silent.
            EqualizerEffectState.CountError(ex.HResult);
        }
    }

    public void DiscardQueuedFrames()
    {
        lock (_gate)
        {
            _discard = true;
        }
    }

    public void Close(MediaEffectClosedReason reason)
    {
    }

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
}
