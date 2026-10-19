namespace Groovefin.Core.Settings;

/// <summary>
/// Holds the current <see cref="AppSettings"/> and persists changes.
/// </summary>
public interface ISettingsService
{
    /// <summary>Gets the current settings. Defaults until <see cref="LoadAsync"/> has completed.</summary>
    AppSettings Current { get; }

    /// <summary>Raised on the caller's thread after <see cref="Current"/> has changed.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// Loads the settings from storage. A missing or unreadable file results in defaults; this method
    /// does not throw for storage problems.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="update"/> to the current settings, raises <see cref="Changed"/> and saves
    /// in the background. Does nothing when the result equals the current settings.
    /// </summary>
    void Update(Func<AppSettings, AppSettings> update);

    /// <summary>Waits until all pending saves have finished. Save errors are logged, not thrown.</summary>
    Task FlushAsync();
}

/// <summary>Event data for <see cref="ISettingsService.Changed"/>.</summary>
public sealed class SettingsChangedEventArgs(AppSettings oldSettings, AppSettings newSettings) : EventArgs
{
    public AppSettings OldSettings { get; } = oldSettings;

    public AppSettings NewSettings { get; } = newSettings;
}
