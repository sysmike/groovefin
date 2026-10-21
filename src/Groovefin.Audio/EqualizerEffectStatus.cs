using System.Runtime.InteropServices;

namespace Groovefin.Audio;

/// <summary>
/// What the equalizer effects did since the DLL was loaded (<see cref="EqualizerEffectContract.GetStatusExport"/>).
/// The effect cannot log, and it never lets an error stop playback; these counters are how its errors reach
/// the app's log. Blittable: it crosses the DLL boundary as it is.
/// </summary>
/// <param name="Buffers">The audio buffers the effects received.</param>
/// <param name="ProcessedBuffers">The buffers they equalized; the others passed through as they were.</param>
/// <param name="Errors">The buffers left as they were because of an error.</param>
/// <param name="LastError">The HRESULT of the last error, or 0.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct EqualizerEffectStatus(long Buffers, long ProcessedBuffers, long Errors, int LastError);
