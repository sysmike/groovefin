using CommunityToolkit.Mvvm.ComponentModel;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// Settings → Start with Windows (#395): the switch shows Windows' state, which Windows may keep from changing
/// (turned off in Task Manager, or by policy); Start minimized is Groovefin's setting. Create it per visit.
/// </summary>
public sealed partial class StartupViewModel(IStartupRegistration startup, ISettingsService settings, IStringResources strings) : ObservableObject, IDisposable
{
    private StartupState _state = StartupState.Off;
    private bool _loaded;
    private bool _busy;

    /// <summary>Gets or sets whether Groovefin starts with Windows; a change goes to Windows at once.</summary>
    public bool StartWithWindows
    {
        get => _state == StartupState.On;
        set
        {
            if (value != StartWithWindows && CanChange)
            {
                _ = ChangeAsync(value);
            }
        }
    }

    /// <summary>Gets a value indicating whether the switch can change: the state is read, and Windows lets it.</summary>
    public bool CanChange => _loaded && !_busy && _state is StartupState.On or StartupState.Off;

    /// <summary>Gets why the switch cannot change, or an empty string.</summary>
    public string StateText => _state switch
    {
        StartupState.OffByUser => strings.GetString("Settings_StartupOffByUser"),
        StartupState.ByPolicy => strings.GetString("Settings_StartupByPolicy"),
        StartupState.Unavailable => strings.GetString("Settings_StartupUnavailable"),
        _ => string.Empty,
    };

    public bool HasStateText => StateText.Length > 0;

    /// <summary>Gets or sets whether a start with Windows opens minimized.</summary>
    public bool StartMinimized
    {
        get => settings.Current.Appearance.StartMinimized;
        set
        {
            if (value != StartMinimized)
            {
                settings.Update(s => s with { Appearance = s.Appearance with { StartMinimized = value } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Reads Windows' state; the switch is off and fixed until then.</summary>
    public async Task LoadAsync()
    {
        _state = await startup.GetStateAsync();
        _loaded = true;
        Changed();
    }

    public void Dispose()
    {
        // Nothing to let go of: the page creates one per visit.
    }

    private async Task ChangeAsync(bool on)
    {
        _busy = true;
        Changed();
        try
        {
            _state = await startup.SetAsync(on);
        }
        finally
        {
            _busy = false;
            Changed();
        }
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(CanChange));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasStateText));
    }
}
