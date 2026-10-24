using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Groovefin.Core.Privacy;

/// <summary>
/// Privacy mode (#234): while it is on, playback is not reported to the server (no play counts, "recently
/// played" or resume points) and the Discord status shows nothing. Never saved: it lasts until it is turned
/// off or the app restarts. Change it on the UI thread.
/// </summary>
public sealed partial class PrivacyMode : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [RelayCommand]
    private void Toggle() => IsOn = !IsOn;
}
