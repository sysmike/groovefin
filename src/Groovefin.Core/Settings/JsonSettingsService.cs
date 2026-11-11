using Groovefin.Core.Notifications;
using Groovefin.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Settings;

/// <summary>
/// <see cref="ISettingsService"/> that stores settings as JSON. Writes go to a temporary file that
/// replaces the settings file, so a crash never leaves a half-written file; the settings before stay as
/// <c>settings.json.bak</c>. A corrupted file gives way to that backup, or to the defaults without one, and is
/// kept as <c>settings.json.corrupt</c>. A file that is there but cannot be read is never replaced (#428): the
/// defaults are used until the next start, and changes are not saved.
/// </summary>
public sealed partial class JsonSettingsService(
    SettingsStoreOptions options,
    IErrorReporter errors,
    ILogger<JsonSettingsService> logger)
    : ISettingsService
{
    private readonly Lock _gate = new();
    private Task _saveChain = Task.CompletedTask;
    private long _version;
    private long _savedVersion;

    // The settings file could not be read: Current is not the user's settings and must not replace the file (#428).
    private bool _unreadable;
    private bool _unreadableReported;

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    private string FilePath => options.FilePath;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // No ConfigureAwait(false): Changed must be raised on the caller's (UI) thread.
        var result = await JsonFile.ReadAsync(FilePath, SettingsJsonContext.Default.AppSettings, cancellationToken);
        var loaded = result.Value ?? new AppSettings();
        switch (result.State)
        {
            case JsonFileState.Loaded when result.FromBackup:
                LogBackupUsed(result.Error, FilePath);
                break;
            case JsonFileState.Corrupt:
                LogCorrupted(result.Error, FilePath, FilePath + JsonFile.CorruptSuffix);
                break;
            case JsonFileState.Unreadable:
                LogReadFailed(result.Error, FilePath);
                break;
        }

        if (loaded.SchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            LogNewerSchema(loaded.SchemaVersion, AppSettings.CurrentSchemaVersion);
        }

        AppSettings old;
        lock (_gate)
        {
            old = Current;
            Current = loaded with { SchemaVersion = AppSettings.CurrentSchemaVersion };
            _savedVersion = _version;
            _unreadable = result.State == JsonFileState.Unreadable;
            _unreadableReported = false;
        }

        if (old != Current)
        {
            Changed?.Invoke(this, new SettingsChangedEventArgs(old, Current));
        }
    }

    public void Update(Func<AppSettings, AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        AppSettings old;
        AppSettings updated;
        lock (_gate)
        {
            old = Current;
            updated = update(old);
            if (updated == old)
            {
                return;
            }

            Current = updated;
            _version++;
            _saveChain = _saveChain.ContinueWith(
                _ => SaveLatestAsync(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }

        Changed?.Invoke(this, new SettingsChangedEventArgs(old, updated));
    }

    public Task FlushAsync()
    {
        lock (_gate)
        {
            return _saveChain;
        }
    }

    private async Task SaveLatestAsync()
    {
        AppSettings snapshot;
        long version;
        bool report;
        lock (_gate)
        {
            if (_savedVersion == _version)
            {
                return;
            }

            snapshot = Current;
            version = _version;
            report = _unreadable && !_unreadableReported;
            _unreadableReported |= _unreadable;
            if (_unreadable && !report)
            {
                return;
            }
        }

        if (report)
        {
            // Said once; the file keeps the user's settings for the next start.
            errors.Report(new IOException($"The settings file '{FilePath}' could not be read, so changes are not saved over it."), Operations.SaveSettings);
            return;
        }

        try
        {
            await JsonFile.WriteAsync(FilePath, snapshot, SettingsJsonContext.Default.AppSettings).ConfigureAwait(false);

            lock (_gate)
            {
                _savedVersion = Math.Max(_savedVersion, version);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logged and shown to the user; the in-memory settings stay valid and the next change retries.
            errors.Report(ex, Operations.SaveSettings);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings file {Path} is corrupted; using defaults and keeping a copy at {Backup}")]
    private partial void LogCorrupted(Exception? exception, string path, string backup);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings file {Path} was gone or corrupted; its backup is used")]
    private partial void LogBackupUsed(Exception? exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not read settings file {Path}; using defaults for now and leaving the file as it is")]
    private partial void LogReadFailed(Exception? exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings file has schema version {FileVersion}, newer than {SupportedVersion}; unknown values are ignored")]
    private partial void LogNewerSchema(int fileVersion, int supportedVersion);
}
