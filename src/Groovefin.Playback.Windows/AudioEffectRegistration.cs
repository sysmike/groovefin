using System.Runtime.InteropServices;
using WinRT;

namespace Groovefin.Playback.Windows;

/// <summary>Asks Windows whether an audio effect's WinRT class can be created, the way Media Foundation will create it.</summary>
internal static class AudioEffectRegistration
{
    /// <summary>IID of <c>IActivationFactory</c>.</summary>
    private static readonly Guid ActivationFactory = new("00000035-0000-0000-C000-000000000046");

    /// <summary>
    /// Gets the class's activation factory, which loads its DLL. Fails when the manifest does not
    /// register the class (REGDB_E_CLASSNOTREG) or the DLL is missing or does not load.
    /// </summary>
    /// <returns>0 (S_OK) if the class can be created, else the HRESULT.</returns>
    public static int Check(string classId)
    {
        var name = MarshalString.FromManaged(classId);
        try
        {
            var result = RoGetActivationFactory(name, in ActivationFactory, out var factory);
            if (result >= 0)
            {
                Marshal.Release(factory);
            }

            return result;
        }
        finally
        {
            MarshalString.DisposeAbi(name);
        }
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);
}
