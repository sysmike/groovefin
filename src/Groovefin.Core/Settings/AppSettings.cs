using Groovefin.Core.Api;
using Groovefin.Core.Navigation;
using Groovefin.Core.Playback;
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

    public StreamingSettings Streaming { get; set; } = new();

    public PlaybackSettings Playback { get; set; } = new();

    public LyricsSettings Lyrics { get; set; } = new();
}

/// <summary>Look and feel.</summary>
public sealed record AppearanceSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Gets or sets a value indicating whether highlights use the Windows accent colour (otherwise neutral grey).</summary>
    public bool UseAccentColor { get; set; } = true;

    /// <summary>Gets or sets how tightly lists and controls are packed.</summary>
    public ListDensity Density { get; set; } = ListDensity.Standard;

    /// <summary>Gets or sets the UI language tag, e.g. <c>de-DE</c>; <see langword="null"/> follows Windows.</summary>
    public string? Language { get; set; }

    /// <summary>Gets or sets the main section shown after start-up (a key of <see cref="PageKeys.MainSections"/>).</summary>
    public string StartPage { get; set; } = PageKeys.Home;

    /// <summary>Gets or sets a value indicating whether the taskbar button shows the playback progress.</summary>
    public bool ShowTaskbarProgress { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether an icon with playback controls is shown in the notification area.</summary>
    public bool ShowTrayIcon { get; set; }

    /// <summary>Gets or sets a value indicating whether minimizing hides the window to the notification area icon.</summary>
    public bool MinimizeToTray { get; set; }
}

/// <summary>How tightly lists and controls are packed.</summary>
public enum ListDensity
{
    /// <summary>The Windows default sizes.</summary>
    Standard,

    /// <summary>Smaller rows and controls, so more fits on the screen.</summary>
    Compact,
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

    public TrackSortField SongSort { get; set; } = TrackSortField.Name;

    public bool SongSortDescending { get; set; }

    /// <summary>Gets or sets the songs table columns in display order; empty means the default layout.</summary>
    public IReadOnlyList<SongColumnSettings> SongColumns { get; set; } = [];
}

/// <summary>A column of the songs table.</summary>
public enum SongColumn
{
    TrackNumber,
    Title,
    Artist,
    Album,
    AlbumArtist,
    Year,
    Duration,
    PlayCount,
    DateAdded,
}

/// <summary>Width and visibility of one songs table column.</summary>
public sealed record SongColumnSettings
{
    public SongColumn Column { get; set; }

    /// <summary>Gets or sets the width in device-independent pixels.</summary>
    public double Width { get; set; }

    public bool IsVisible { get; set; } = true;
}

/// <summary>
/// Stream quality. Local servers (home network) default to the original files, remote ones to
/// 320 kbit/s, so mobile data and slow uplinks are spared.
/// </summary>
public sealed record StreamingSettings
{
    /// <summary>Gets or sets the bitrate limit on the local network; <see langword="null"/> means no limit.</summary>
    public int? LocalMaxBitrateKbps { get; set; }

    /// <summary>Gets or sets the bitrate limit for remote servers; <see langword="null"/> means no limit.</summary>
    public int? RemoteMaxBitrateKbps { get; set; } = 320;

    public TranscodingCodec Codec { get; set; } = TranscodingCodec.Aac;

    /// <summary>Gets the stream options for a server at <paramref name="serverUrl"/>.</summary>
    public StreamOptions OptionsFor(Uri serverUrl) =>
        new(NetworkLocation.IsLocal(serverUrl) ? LocalMaxBitrateKbps : RemoteMaxBitrateKbps, Codec);
}

/// <summary>Playback: loudness, instant mix, resuming the last session and the Discord status.</summary>
public sealed record PlaybackSettings
{
    public NormalizationMode Normalization { get; set; } = NormalizationMode.Off;

    public InstantMixMode InstantMix { get; set; } = InstantMixMode.Replace;

    /// <summary>Gets or sets a value indicating whether the queue and position are restored (paused) on the next start.</summary>
    public bool ResumeSession { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether Discord shows the playing track in the user's status.</summary>
    public bool DiscordPresence { get; set; }
}

/// <summary>How lines are aligned in the lyrics pane.</summary>
public enum LyricsAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>Look of the lyrics pane.</summary>
public sealed record LyricsSettings
{
    /// <summary>Gets or sets the font size in device-independent pixels.</summary>
    public double FontSize { get; set; } = 20;

    public LyricsAlignment Alignment { get; set; } = LyricsAlignment.Center;
}
