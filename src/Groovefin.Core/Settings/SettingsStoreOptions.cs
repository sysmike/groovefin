namespace Groovefin.Core.Settings;

/// <summary>
/// Location of the settings file. The app resolves it (packaged: <c>LocalFolder</c>,
/// unpackaged: <c>%LOCALAPPDATA%\Groovefin</c>).
/// </summary>
/// <param name="FilePath">Absolute path of <c>settings.json</c>.</param>
public sealed record SettingsStoreOptions(string FilePath);
