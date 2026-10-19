using Groovefin.Core.Models;
using Jellyfin.Sdk.Generated.Models;

namespace Groovefin.Jellyfin.Mapping;

/// <summary>Maps SDK DTOs to Core domain models. SDK types never leave this project.</summary>
internal static class ItemMapper
{
    public static Album ToAlbum(BaseItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Album
        {
            Id = FormatId(dto.Id) ?? throw new ArgumentException("Album without ID.", nameof(dto)),
            Name = dto.Name ?? string.Empty,
            AlbumArtist = dto.AlbumArtist,
            AlbumArtists = ToArtistRefs(dto.AlbumArtists),
            Year = dto.ProductionYear,
            Duration = dto.RunTimeTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null,
            PrimaryImageTag = PrimaryImageTag(dto),
            IsFavorite = dto.UserData?.IsFavorite ?? false,
            PlayCount = dto.UserData?.PlayCount ?? 0,
            DateAdded = dto.DateCreated,
        };
    }

    public static Track ToTrack(BaseItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Track
        {
            Id = FormatId(dto.Id) ?? throw new ArgumentException("Track without ID.", nameof(dto)),
            Name = dto.Name ?? string.Empty,
            AlbumId = FormatId(dto.AlbumId),
            AlbumName = dto.Album,
            AlbumImageTag = dto.AlbumPrimaryImageTag,
            Artists = ToArtistRefs(dto.ArtistItems),
            Duration = dto.RunTimeTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null,
            TrackNumber = dto.IndexNumber,
            DiscNumber = dto.ParentIndexNumber,
            IsFavorite = dto.UserData?.IsFavorite ?? false,
            PlayCount = dto.UserData?.PlayCount ?? 0,
            NormalizationGain = dto.NormalizationGain,
            AlbumNormalizationGain = dto.AlbumNormalizationGain,
        };
    }

    public static Artist ToArtist(BaseItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Artist
        {
            Id = FormatId(dto.Id) ?? throw new ArgumentException("Artist without ID.", nameof(dto)),
            Name = dto.Name ?? string.Empty,
            PrimaryImageTag = PrimaryImageTag(dto),
            IsFavorite = dto.UserData?.IsFavorite ?? false,
            Biography = PlainText.FromHtml(dto.Overview),
            Genres = dto.Genres?.Where(g => !string.IsNullOrWhiteSpace(g)).ToList() ?? [],
        };
    }

    /// <summary>Formats a Jellyfin ID the way the server writes it in URLs (32 hex digits).</summary>
    public static string? FormatId(Guid? id) => id is { } value && value != Guid.Empty ? value.ToString("N") : null;

    private static List<ArtistRef> ToArtistRefs(List<NameGuidPair>? pairs) =>
        pairs?
            .Where(p => p.Id is not null && p.Id != Guid.Empty)
            .Select(p => new ArtistRef(FormatId(p.Id)!, p.Name ?? string.Empty))
            .ToList() ?? [];

    private static string? PrimaryImageTag(BaseItemDto dto) =>
        dto.ImageTags?.AdditionalData is { } tags && tags.TryGetValue("Primary", out var tag) ? tag?.ToString() : null;
}
