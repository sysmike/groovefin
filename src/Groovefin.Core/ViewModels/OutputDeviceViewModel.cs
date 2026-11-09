using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Groovefin.Core.AudioOutput;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// Settings → Playback → Output device (#389): the Windows default first, then the devices Windows has now. A chosen
/// device that is not connected stays listed, marked so, and the music plays on the default meanwhile.
/// </summary>
public sealed partial class OutputDeviceViewModel(IAudioOutput output, ISettingsService settings, IStringResources strings) : ObservableObject, IDisposable
{
    private List<AudioOutputDevice?> _devices = [null];
    private bool _loading;

    /// <summary>Gets the names in the list; the first is the Windows default.</summary>
    public ObservableCollection<string> Options { get; } = [strings.GetString("Settings_OutputDefault")];

    /// <summary>Gets or sets the chosen entry of <see cref="Options"/>.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    /// <summary>Lists the devices and selects the saved one.</summary>
    public async Task LoadAsync()
    {
        IReadOnlyList<AudioOutputDevice> devices;
        try
        {
            devices = await output.GetDevicesAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            devices = [];
        }

        var playback = settings.Current.Playback;
        List<AudioOutputDevice?> list = [null, .. devices];
        if (playback.OutputDeviceId is { } id && devices.All(d => d.Id != id))
        {
            var name = playback.OutputDeviceName ?? strings.GetString("Settings_OutputUnknown");
            list.Add(new AudioOutputDevice(id, strings.GetString("Settings_OutputMissing").Replace("{0}", name, StringComparison.Ordinal)));
        }

        _loading = true;
        try
        {
            _devices = list;
            Options.Clear();
            Options.Add(strings.GetString("Settings_OutputDefault"));
            foreach (var device in list.Skip(1))
            {
                Options.Add(device!.Name);
            }

            // The list was refilled: select again, even the same entry, so the combo box shows it.
            SelectedIndex = -1;
            SelectedIndex = Math.Max(0, list.FindIndex(d => d?.Id == playback.OutputDeviceId));
        }
        finally
        {
            _loading = false;
        }
    }

    public void Dispose()
    {
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= _devices.Count)
        {
            return;
        }

        var device = _devices[value];
        if (device?.Id == settings.Current.Playback.OutputDeviceId)
        {
            return;
        }

        settings.Update(s => s with { Playback = s.Playback with { OutputDeviceId = device?.Id, OutputDeviceName = device?.Name } });
    }
}
