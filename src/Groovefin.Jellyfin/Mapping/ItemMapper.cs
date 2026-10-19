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
