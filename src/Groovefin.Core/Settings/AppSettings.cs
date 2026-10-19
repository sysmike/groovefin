using Groovefin.Core.Services;

namespace Groovefin.Core.Settings;

/// <summary>
/// All persisted user settings. Properties that are missing in an older settings file keep these defaults.
/// </summary>
/// <remarks>
/// Treat instances as immutable and change settings only with <see cref="ISettingsService.Update"/> and
/// <c>with</c> expressions. The setters are public only because the source-generated JSON serializer
/// assigns <c>init</c> properties even when they are missing from the file, which would replace the
/// defaults with <see langword="null"/>.
/// </remarks>
public sealed record AppSettings
{
    /// <summary>The schema version this build writes. Increase it when a migration is needed.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public AppearanceSettings Appearance { get; set; } = new();

    public CacheSettings Cache { get; set; } = new();
}

/// <summary>Look and feel.</summary>
public sealed record AppearanceSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
}

/// <summary>Local caches.</summary>
public sealed record CacheSettings
{
    /// <summary>Gets or sets the image cache size limit in megabytes.</summary>
    public int ImageCacheSizeMegabytes { get; set; } = 500;
}
