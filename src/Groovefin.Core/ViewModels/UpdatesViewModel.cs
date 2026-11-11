using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Updates;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// Settings → About → updates (#432): the switch for the daily check, what the last check found with a link to
/// the release, and Check now. Create it per visit.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject, IDisposable
{
    private readonly UpdateChecker _checker;
    private readonly ISettingsService _settings;
    private readonly IStringResources _strings;
    private readonly IDispatcher _dispatcher;
    private bool _failed;

    public UpdatesViewModel(UpdateChecker checker, ISettingsService settings, IStringResources strings, IDispatcher dispatcher)
    {
        _checker = checker;
        _settings = settings;
        _strings = strings;
        _dispatcher = dispatcher;
        _checker.Changed += OnCheckerChanged;
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>Gets or sets whether Groovefin asks for a newer release once a day.</summary>
    public bool CheckAutomatically
    {
        get => _settings.Current.Updates.CheckAutomatically;
        set
        {
            if (value != CheckAutomatically)
            {
                _settings.Update(s => s with { Updates = s.Updates with { CheckAutomatically = value } });
            }
        }
    }

    /// <summary>Gets a value indicating whether a check runs now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    public partial bool IsChecking { get; private set; }

    /// <summary>Gets a value indicating whether a newer release is out.</summary>
    public bool HasUpdate => _checker.Available is not null;

    /// <summary>Gets the newer release's page, or <see langword="null"/>.</summary>
    public Uri? ReleaseUri => _checker.Available?.Page;

    /// <summary>Gets what the last check found, e.g. "Groovefin 1.2.0 is available."</summary>
    public string StatusText
    {
        get
        {
            if (IsChecking)
            {
                return _strings.GetString("Updates_Checking");
            }

            if (_checker.Available is { } release)
            {
                return _strings.GetString("Updates_Available").Replace("{0}", release.Version, StringComparison.Ordinal);
            }

            return _strings.GetString(_failed ? "Updates_Failed" : _checker.HasChecked ? "Updates_UpToDate" : "Updates_NotChecked");
        }
    }

    public void Dispose()
    {
        _checker.Changed -= OnCheckerChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    private bool CanCheckNow() => !IsChecking;

    /// <summary>Asks now, whatever the switch says.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNowAsync()
    {
        IsChecking = true;
        try
        {
            _failed = await _checker.CheckAsync(manual: true) == UpdateCheckResult.Failed;
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
        finally
        {
            IsChecking = false;
            Refresh();
        }
    }

    private void OnCheckerChanged(object? sender, EventArgs e) => _ = _dispatcher.EnqueueAsync(Refresh);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Updates.CheckAutomatically != e.NewSettings.Updates.CheckAutomatically)
        {
            OnPropertyChanged(nameof(CheckAutomatically));
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(ReleaseUri));
        OnPropertyChanged(nameof(StatusText));
    }
}
