using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Settings;

/// <summary>
/// <see cref="ISettingsService"/> that stores settings as JSON. Writes go to a temporary file that
/// replaces the settings file, so a crash never leaves a half-written file. A corrupted file is kept
/// as <c>settings.json.corrupt</c> and the defaults are used.
/// </summary>
public sealed partial class JsonSettingsService(SettingsStoreOptions options, ILogger<JsonSettingsService> logger)
    : ISettingsService
{
    private readonly Lock _gate = new();
    private Task _saveChain = Task.CompletedTask;
    private long _version;
    private long _savedVersion;

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    private string FilePath => options.FilePath;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AppSettings loaded;
        try
        {
            // No ConfigureAwait(false): Changed must be raised on the caller's (UI) thread.
            loaded = await ReadAsync(cancellationToken) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(ex, FilePath);
            loaded = new AppSettings();
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

    private async Task<AppSettings?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync(stream, SettingsJsonContext.Default.AppSettings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            var backup = FilePath + ".corrupt";
            LogCorrupted(ex, FilePath, backup);
            File.Move(FilePath, backup, overwrite: true);
            return null;
        }
    }

    private async Task SaveLatestAsync()
    {
        AppSettings snapshot;
        long version;
        lock (_gate)
        {
            if (_savedVersion == _version)
            {
                return;
            }

            snapshot = Current;
            version = _version;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SettingsJsonContext.Default.AppSettings).ConfigureAwait(false);
            }

            File.Move(temp, FilePath, overwrite: true);

            lock (_gate)
            {
                _savedVersion = Math.Max(_savedVersion, version);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // TODO(#13): surface the failure in an InfoBar through the notification service.
            LogWriteFailed(ex, FilePath);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings file {Path} is corrupted; using defaults and keeping a copy at {Backup}")]
    private partial void LogCorrupted(Exception exception, string path, string backup);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not read settings file {Path}; using defaults")]
    private partial void LogReadFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not save settings file {Path}")]
    private partial void LogWriteFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings file has schema version {FileVersion}, newer than {SupportedVersion}; unknown values are ignored")]
    private partial void LogNewerSchema(int fileVersion, int supportedVersion);
}
