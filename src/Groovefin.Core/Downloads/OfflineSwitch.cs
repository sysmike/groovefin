using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Downloads;

/// <summary>
/// "Work offline" (#400): while it is on, Groovefin behaves as without a network although the server can be
/// reached. The pages show the downloads, only downloaded songs play (the song playing goes on) and nothing
/// downloads. It is saved (<see cref="DownloadSettings.WorkOffline"/>), so it stays on after a restart.
/// It is also the <see cref="INetworkStatus"/> the app sees: the PC's, offline while the switch is on. Change it on
/// the UI thread.
/// </summary>
public sealed partial class OfflineSwitch : ObservableObject, INetworkStatus, IDisposable
{
    private readonly INetworkStatus _network;
    private readonly ISettingsService _settings;

    public OfflineSwitch(INetworkStatus network, ISettingsService settings)
    {
        _network = network;
        _settings = settings;
        _network.Changed += OnNetworkChanged;
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>Raised on any thread when the PC's network changed, and on the UI thread when the switch did.</summary>
    public event EventHandler? Changed;

    public bool IsOn
    {
        get => _settings.Current.Downloads.WorkOffline;
        set => _settings.Update(s => s with { Downloads = s.Downloads with { WorkOffline = value } });
    }

    public bool IsOnline => !IsOn && _network.IsOnline;

    public bool IsMetered => _network.IsMetered;

    public void Dispose()
    {
        _network.Changed -= OnNetworkChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    [RelayCommand]
    private void Toggle() => IsOn = !IsOn;

    private void OnNetworkChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Downloads.WorkOffline != e.NewSettings.Downloads.WorkOffline)
        {
            OnPropertyChanged(nameof(IsOn));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
