using System.Runtime.InteropServices;

namespace Groovefin.Audio.Effects;

/// <summary>
/// The parameters every <see cref="EqualizerEffect"/> in the process plays with, and their counters. The app
/// reaches them through the DLL's exports (<see cref="EqualizerEffectContract"/>), not through the effect's
/// property set: Media Foundation did not pass changes to that on (#169).
/// </summary>
internal static unsafe class EqualizerEffectState
{
    private const int InvalidArgument = unchecked((int)0x80070057);

    /// <summary>How long a request to fade in the next stream holds: the restarted stream opens well within it.</summary>
    private const long FadeInRequestMilliseconds = 5000;

    private static EqualizerParameters _parameters = EqualizerParameters.Off;
    private static long _buffers;
    private static long _processed;
    private static long _errors;
    private static int _lastError;

    /// <summary>When the app asked for the next stream to fade in (<see cref="Environment.TickCount64"/>), or 0.</summary>
    private static long _fadeInRequested;

    /// <summary>Gets the parameters set last. A new instance for each change, so a reference comparison finds changes.</summary>
    public static EqualizerParameters Parameters => Volatile.Read(ref _parameters);

    public static void CountBuffer() => Interlocked.Increment(ref _buffers);

    public static void CountProcessed() => Interlocked.Increment(ref _processed);

    public static void CountError(int result)
    {
        Volatile.Write(ref _lastError, result);
        Interlocked.Increment(ref _errors);
    }

    /// <summary>Takes a recent request to fade in the next stream: only the first stream to start gets it.</summary>
    public static bool TakeFadeInRequest()
    {
        var requested = Interlocked.Exchange(ref _fadeInRequested, 0);
        return requested != 0 && Environment.TickCount64 - requested <= FadeInRequestMilliseconds;
    }

    [UnmanagedCallersOnly(EntryPoint = EqualizerEffectContract.FadeInNextStreamExport)]
    private static int FadeInNextStream()
    {
        Interlocked.Exchange(ref _fadeInRequested, Math.Max(1, Environment.TickCount64));
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = EqualizerEffectContract.SetParametersExport)]
    private static int SetParameters(char* text, int length)
    {
        if (text is null || length < 0 || !EqualizerParameters.TryParse(new string(text, 0, length), out var parameters))
        {
            return InvalidArgument;
        }

        Volatile.Write(ref _parameters, parameters);
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = EqualizerEffectContract.GetStatusExport)]
    private static int GetStatus(EqualizerEffectStatus* status)
    {
        if (status is null)
        {
            return InvalidArgument;
        }

        *status = new EqualizerEffectStatus(
            Interlocked.Read(ref _buffers),
            Interlocked.Read(ref _processed),
            Interlocked.Read(ref _errors),
            Volatile.Read(ref _lastError));
        return 0;
    }
}
