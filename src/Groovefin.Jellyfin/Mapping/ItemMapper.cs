using System.Text.Json;
using Groovefin.Core.Models;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Kiota.Abstractions.Serialization;

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
            Genres = ToGenres(dto.GenreItems),
            MusicBrainzReleaseGroupId = ProviderId(dto.ProviderIds, "MusicBrainzReleaseGroup"),
            MusicBrainzReleaseId = ProviderId(dto.ProviderIds, "MusicBrainzAlbum"),
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
            AlbumArtist = dto.AlbumArtist,
            Artists = ToArtistRefs(dto.ArtistItems),
            Duration = dto.RunTimeTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null,
            TrackNumber = dto.IndexNumber,
            DiscNumber = dto.ParentIndexNumber,
            Year = dto.ProductionYear,
            DateAdded = dto.DateCreated,
            IsFavorite = dto.UserData?.IsFavorite ?? false,
            PlayCount = dto.UserData?.PlayCount ?? 0,
            LastPlayed = dto.UserData?.LastPlayedDate,
            NormalizationGain = dto.NormalizationGain,
            AlbumNormalizationGain = dto.AlbumNormalizationGain,
        };
    }

    /// <summary>Maps an item loaded with all fields (/Items/{id}) for the info dialog (#282).</summary>
    public static TrackDetails ToTrackDetails(BaseItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var source = dto.MediaSources?.FirstOrDefault();
        var audio = (source?.MediaStreams ?? dto.MediaStreams)?.FirstOrDefault(s => s.Type == MediaStream_Type.Audio);
        return new TrackDetails
        {
            Track = ToTrack(dto),
            AlbumArtists = [.. (dto.AlbumArtists ?? []).Select(a => a.Name).OfType<string>()],
            Genres = dto.Genres ?? [],
            ReleaseDate = dto.PremiereDate is { } date ? DateOnly.FromDateTime(date.UtcDateTime) : null,
            People = [.. (dto.People ?? []).Select(ToPerson).OfType<TrackPerson>()],
            Studios = [.. (dto.Studios ?? []).Select(s => s.Name).OfType<string>()],
            Tags = dto.Tags ?? [],
            ProviderIds = ProviderIds(dto.ProviderIds),
            HasLyrics = dto.HasLyrics,
            Container = source?.Container ?? dto.Container,
            Codec = audio?.Codec,
            Bitrate = audio?.BitRate ?? source?.Bitrate,
            SampleRate = audio?.SampleRate,
            BitDepth = audio?.BitDepth,
            Channels = audio?.Channels,
            Size = source?.Size,
            Path = source?.Path ?? dto.Path,
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
            Genres = ToGenres(dto.GenreItems),
            MusicBrainzArtistId = ProviderId(dto.ProviderIds, "MusicBrainzArtist"),
        };
    }

    /// <summary>Formats a Jellyfin ID the way the server writes it in URLs (32 hex digits).</summary>
    public static string? FormatId(Guid? id) => id is { } value && value != Guid.Empty ? value.ToString("N") : null;

    private static List<Genre> ToGenres(List<NameGuidPair>? pairs) =>
        pairs?
            .Where(p => p.Id is not null && p.Id != Guid.Empty && !string.IsNullOrEmpty(p.Name))
            .Select(p => new Genre(FormatId(p.Id)!, p.Name!))
            .ToList() ?? [];

    /// <summary>
    /// Gets a provider ID such as "MusicBrainzAlbum"; the SDK keeps them as untyped additional data. It reads
    /// a value that looks like a GUID as a <see cref="Guid"/>, and MusicBrainz IDs always do: they come back
    /// in MusicBrainz's form, lower case with dashes (#194).
    /// </summary>
    private static string? ProviderId(BaseItemDto_ProviderIds? ids, string key)
    {
        if (ids?.AdditionalData is not { } data || !data.TryGetValue(key, out var value))
        {
            return null;
        }

        var text = value switch
        {
            string s => s,
            UntypedString u => u.GetValue(),
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            Guid guid => guid.ToString("D"),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>All provider IDs with a value, sorted by key.</summary>
    private static List<KeyValuePair<string, string>> ProviderIds(BaseItemDto_ProviderIds? ids) =>
        ids?.AdditionalData is not { } data
            ? []
            : [.. data.Keys
                .Order(StringComparer.Ordinal)
                .Select(key => (key, value: ProviderId(ids, key)))
                .Where(p => p.value is not null)
                .Select(p => KeyValuePair.Create(p.key, p.value!))];

    /// <summary>A contributor; the artists are already on the track, so they are left out.</summary>
    private static TrackPerson? ToPerson(BaseItemPerson person)
    {
        if (string.IsNullOrWhiteSpace(person.Name))
        {
            return null;
        }

        PersonRole? role = person.Type switch
        {
            BaseItemPerson_Type.Artist or BaseItemPerson_Type.AlbumArtist => null,
            BaseItemPerson_Type.Composer => PersonRole.Composer,
            BaseItemPerson_Type.Lyricist => PersonRole.Lyricist,
            BaseItemPerson_Type.Writer => PersonRole.Writer,
            BaseItemPerson_Type.Arranger => PersonRole.Arranger,
            BaseItemPerson_Type.Conductor => PersonRole.Conductor,
            BaseItemPerson_Type.Producer => PersonRole.Producer,
            BaseItemPerson_Type.Engineer => PersonRole.Engineer,
            BaseItemPerson_Type.Mixer => PersonRole.Mixer,
            BaseItemPerson_Type.Remixer => PersonRole.Remixer,
            _ => PersonRole.Other,
        };
        return role is { } value ? new TrackPerson(person.Name, value) : null;
    }

    private static List<ArtistRef> ToArtistRefs(List<NameGuidPair>? pairs) =>
        pairs?
            .Where(p => p.Id is not null && p.Id != Guid.Empty)
            .Select(p => new ArtistRef(FormatId(p.Id)!, p.Name ?? string.Empty))
            .ToList() ?? [];

    public static Playlist ToPlaylist(BaseItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Playlist
        {
            Id = FormatId(dto.Id) ?? throw new ArgumentException("Playlist without ID.", nameof(dto)),
            Name = dto.Name ?? string.Empty,
            PrimaryImageTag = PrimaryImageTag(dto),
            TrackCount = dto.ChildCount,
            Duration = dto.RunTimeTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null,
            IsFavorite = dto.UserData?.IsFavorite ?? false,
        };
    }

    private static string? PrimaryImageTag(BaseItemDto dto) =>
        dto.ImageTags?.AdditionalData is { } tags && tags.TryGetValue("Primary", out var tag) ? tag?.ToString() : null;
}
