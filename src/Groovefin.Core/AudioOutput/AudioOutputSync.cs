using Groovefin.Core.Settings;

namespace Groovefin.Core.AudioOutput;

/// <summary>Passes the chosen output device (#389) to the player: at start-up and after each change. Lives as long as the app.</summary>
public sealed class AudioOutputSync : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IAudioOutput _output;

    public AudioOutputSync(ISettingsService settings, IAudioOutput output)
    {
        _settings = settings;
        _output = output;
        _settings.Changed += OnSettingsChanged;
        if (settings.Current.Playback.OutputDeviceId is { } id)
        {
            _output.Use(id);
        }
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var id = e.NewSettings.Playback.OutputDeviceId;
        if (id != e.OldSettings.Playback.OutputDeviceId)
        {
            _output.Use(id);
        }
    }
}
