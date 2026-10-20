using Groovefin.Core.Settings;

namespace Groovefin.Core.Equalizer;

/// <summary>
/// Passes the equalizer settings to the playback engine: at start-up and after each change. Lives as long
/// as the app.
/// </summary>
public sealed class EqualizerSync : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IAudioEqualizer _equalizer;

    public EqualizerSync(ISettingsService settings, IAudioEqualizer equalizer)
    {
        _settings = settings;
        _equalizer = equalizer;
        _settings.Changed += OnSettingsChanged;
        if (_equalizer.IsAvailable)
        {
            _equalizer.Apply(settings.Current.Playback.Equalizer.ToParameters());
        }
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var parameters = e.NewSettings.Playback.Equalizer.ToParameters();
        var old = e.OldSettings.Playback.Equalizer.ToParameters();

        // While it stays off (or flat) the audio passes through as it is: nothing to tell the effect.
        if (_equalizer.IsAvailable && parameters != old && (parameters.IsActive || old.IsActive))
        {
            _equalizer.Apply(parameters);
        }
    }
}
