using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Audio;
using Groovefin.Core.Equalizer;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>A slider of the equalizer: one band, its frequency and gain.</summary>
public sealed class EqualizerBandViewModel : ObservableObject
{
    private readonly EqualizerViewModel _owner;
    private double _gain;

    internal EqualizerBandViewModel(EqualizerViewModel owner, int index, string name, double gain)
    {
        _owner = owner;
        Index = index;
        Name = name;
        _gain = gain;
    }

    public int Index { get; }

    /// <summary>Gets the centre frequency, e.g. "31 Hz" or "16 kHz": the label and the slider's accessible name.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the gain in dB; moving a slider switches to the custom preset.</summary>
    public double Gain
    {
        get => _gain;
        set
        {
            if (Refresh(value))
            {
                _owner.SetGain(Index, value);
            }
        }
    }

    /// <summary>Gets the gain as text, e.g. "+3 dB".</summary>
    public string GainText => _owner.FormatDecibels(Gain);

    internal bool Refresh(double gain)
    {
        if (!SetProperty(ref _gain, gain, nameof(Gain)))
        {
            return false;
        }

        OnPropertyChanged(nameof(GainText));
        return true;
    }
}

/// <summary>
/// The equalizer in Settings: on or off, the presets, a slider per band and the preamp. Every change is
/// saved and heard at once (<see cref="EqualizerSync"/>). Create one per visit of the page and dispose it.
/// </summary>
public sealed partial class EqualizerViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IAudioEqualizer _equalizer;
    private readonly IStringResources _strings;

    public EqualizerViewModel(ISettingsService settings, IAudioEqualizer equalizer, IStringResources strings)
    {
        _settings = settings;
        _equalizer = equalizer;
        _strings = strings;
        PresetOptions = [.. EqualizerPresets.All.Select(p => strings.GetString($"EqualizerPreset_{p}"))];
        var gains = Current.Gains;
        Bands = [.. EqualizerBands.Frequencies.Select((f, i) => new EqualizerBandViewModel(this, i, FormatFrequency(f), gains[i]))];
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>Gets a value indicating whether the playback engine has the equalizer; if not, it is not shown.</summary>
    public bool IsAvailable => _equalizer.IsAvailable;

    public bool IsEnabled
    {
        get => Current.Enabled;
        set
        {
            if (value != IsEnabled)
            {
                Update(e => e with { Enabled = value });
            }
        }
    }

    /// <summary>Gets the names of <see cref="EqualizerPresets.All"/>.</summary>
    public IReadOnlyList<string> PresetOptions { get; }

    /// <summary>Gets or sets the chosen preset as an index into <see cref="PresetOptions"/>.</summary>
    public int PresetIndex
    {
        get => Math.Max(0, Array.IndexOf(Enum.GetValues<EqualizerPreset>(), Current.Preset));
        set
        {
            if (value >= 0 && value < EqualizerPresets.All.Count && value != PresetIndex)
            {
                Update(e => e with { Preset = EqualizerPresets.All[value] });
            }
        }
    }

    /// <summary>Gets the band sliders, lowest frequency first.</summary>
    public IReadOnlyList<EqualizerBandViewModel> Bands { get; }

    /// <summary>Gets or sets the preamp in dB.</summary>
    public double Preamp
    {
        get => Current.Preamp;
        set
        {
            var preamp = EqualizerBands.Clamp(value);
            if (preamp != Preamp)
            {
                Update(e => e with { Preamp = preamp });
            }
        }
    }

    /// <summary>Gets the preamp as text, e.g. "-3 dB".</summary>
    public string PreampText => FormatDecibels(Preamp);

    public double MinGain => EqualizerBands.MinGain;

    public double MaxGain => EqualizerBands.MaxGain;

    private EqualizerSettings Current => _settings.Current.Playback.Equalizer;

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    /// <summary>Formats a gain with its sign, e.g. "+3 dB", "0 dB", "-2.5 dB".</summary>
    internal string FormatDecibels(double gain) =>
        Replace(_strings.GetString("Equalizer_Decibels"), gain.ToString("+0.#;-0.#;0", CultureInfo.CurrentCulture));

    /// <summary>Sets a band from its slider: the shown gains become the custom preset.</summary>
    internal void SetGain(int index, double gain)
    {
        var gains = Current.Gains.ToArray();
        gains[index] = EqualizerBands.Clamp(gain);
        Update(e => e with { Preset = EqualizerPreset.Custom, CustomGains = gains });
    }

    /// <summary>All bands and the preamp back to 0 dB (the flat preset); on or off stays.</summary>
    [RelayCommand]
    private void Reset() =>
        Update(e => e with { Preset = EqualizerPreset.Flat, CustomGains = new double[EqualizerBands.Count], Preamp = 0 });

    private void Update(Func<EqualizerSettings, EqualizerSettings> update) =>
        _settings.Update(s => s with { Playback = s.Playback with { Equalizer = update(s.Playback.Equalizer) } });

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var old = e.OldSettings.Playback.Equalizer;
        var now = e.NewSettings.Playback.Equalizer;
        if (old == now)
        {
            return;
        }

        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(PresetIndex));
        OnPropertyChanged(nameof(Preamp));
        OnPropertyChanged(nameof(PreampText));
        var gains = now.Gains;
        foreach (var band in Bands)
        {
            band.Refresh(gains[band.Index]);
        }
    }

    private string FormatFrequency(double hertz) => hertz >= 1000
        ? Replace(_strings.GetString("Equalizer_Kilohertz"), (hertz / 1000).ToString("0.#", CultureInfo.CurrentCulture))
        : Replace(_strings.GetString("Equalizer_Hertz"), hertz.ToString("0", CultureInfo.CurrentCulture));

    private static string Replace(string format, string value) => format.Replace("{0}", value, StringComparison.Ordinal);
}
