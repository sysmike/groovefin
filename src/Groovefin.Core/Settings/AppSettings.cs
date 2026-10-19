using Groovefin.Core.Api;
using Groovefin.Core.Servers;
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

    public ServerSettings Servers { get; set; } = new();

    public CacheSettings Cache { get; set; } = new();

    public BrowseSettings Browse { get; set; } = new();
}

/// <summary>Look and feel.</summary>
public sealed record AppearanceSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
}

/// <summary>Known servers and the active one. Access tokens are in the credential store, not here.</summary>
public sealed record ServerSettings
{
    public IReadOnlyList<ServerProfile> Known { get; set; } = [];

    /// <summary>Gets or sets the <see cref="ServerProfile.Id"/> of the active server.</summary>
    public string? ActiveServerId { get; set; }
}

/// <summary>Local caches.</summary>
public sealed record CacheSettings
{
    /// <summary>Gets or sets the image cache size limit in megabytes.</summary>
    public int ImageCacheSizeMegabytes { get; set; } = 500;
}

/// <summary>How a collection page shows its items.</summary>
public enum CollectionViewMode
{
    Grid,
    List,
}

/// <summary>Remembered view options of the browsing pages.</summary>
public sealed record BrowseSettings
{
    public CollectionViewMode AlbumView { get; set; } = CollectionViewMode.Grid;

    public AlbumSortField AlbumSort { get; set; } = AlbumSortField.Name;

    public bool AlbumSortDescending { get; set; }

    public CollectionViewMode ArtistView { get; set; } = CollectionViewMode.Grid;

    public ArtistSortField ArtistSort { get; set; } = ArtistSortField.Name;

    public bool ArtistSortDescending { get; set; }

    /// <summary>Gets or sets a value indicating whether the artists page lists album artists only.</summary>
    public bool AlbumArtistsOnly { get; set; } = true;
}
