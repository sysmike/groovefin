using System.Runtime.InteropServices;
using Groovefin.Audio;

namespace Groovefin.Playback.Windows;

/// <summary>
/// The equalizer effect DLL's exports (<see cref="EqualizerEffectContract"/>): how the parameters reach the
/// effects MediaPlayer created from it, and how their counters come back (#169). The DLL is loaded from the
/// app's directory, where Media Foundation loads it from too, so both use the same copy.
/// </summary>
public static class EqualizerEffectLibrary
{
    /// <summary>Sets the parameters all equalizer effects use from their next buffer on.</summary>
    /// <exception cref="DllNotFoundException">The DLL is missing.</exception>
    /// <exception cref="EntryPointNotFoundException">The DLL is an older one without the export.</exception>
    public static void SetParameters(EqualizerParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var text = parameters.Format();
        Marshal.ThrowExceptionForHR(GroovefinEqualizerSetParameters(text, text.Length));
    }

    /// <summary>Gets what the equalizer effects did so far.</summary>
    /// <inheritdoc cref="SetParameters" path="/exception"/>
    public static EqualizerEffectStatus GetStatus()
    {
        Marshal.ThrowExceptionForHR(GroovefinEqualizerGetStatus(out var status));
        return status;
    }

    /// <summary>Lets the next stream an effect starts on begin softly: a transcode restarted at a seek target (#283).</summary>
    /// <inheritdoc cref="SetParameters" path="/exception"/>
    public static void FadeInNextStream() => Marshal.ThrowExceptionForHR(GroovefinEqualizerFadeInNextStream());

    [DllImport(EqualizerEffectContract.LibraryName, ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static extern int GroovefinEqualizerSetParameters(string text, int length);

    [DllImport(EqualizerEffectContract.LibraryName, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static extern int GroovefinEqualizerGetStatus(out EqualizerEffectStatus status);

    [DllImport(EqualizerEffectContract.LibraryName, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static extern int GroovefinEqualizerFadeInNextStream();
}
