using System.Globalization;
using Groovefin.Core.Formatting;
using Groovefin.Core.Models;
using Groovefin.Core.Services;

namespace Groovefin.Core.Menus;

/// <summary>
/// The rows of a track's info dialog (#282), under the headings Tags, File and Library. Fields the server
/// doesn't know are left out, and so is a heading without rows. Numbers and dates use the current culture.
/// </summary>
public static class TrackInfo
{
    /// <summary>Labels of the MusicBrainz IDs; other provider IDs are labelled with their key.</summary>
    private static readonly Dictionary<string, string> ProviderLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MusicBrainzTrack"] = "Info_MusicBrainzTrack",
        ["MusicBrainzRecording"] = "Info_MusicBrainzRecording",
        ["MusicBrainzAlbum"] = "Info_MusicBrainzAlbum",
        ["MusicBrainzReleaseGroup"] = "Info_MusicBrainzReleaseGroup",
        ["MusicBrainzArtist"] = "Info_MusicBrainzArtist",
        ["MusicBrainzAlbumArtist"] = "Info_MusicBrainzAlbumArtist",
    };

    public static IReadOnlyList<DetailSection> Sections(TrackDetails details, IStringResources strings)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(strings);
        var culture = CultureInfo.CurrentCulture;
        var track = details.Track;
        string Format(string key, string value) => strings.GetString(key).Replace("{0}", value, StringComparison.Ordinal);
        string YesNo(bool value) => strings.GetString(value ? "Info_Yes" : "Info_No");

        var tags = new Rows(strings);
        tags.Add("Info_Title", track.Name);
        tags.Add("Info_Artists", Join(track.Artists.Select(a => a.Name)));
        tags.Add("Info_Album", track.AlbumName);
        tags.Add("Info_AlbumArtists", details.AlbumArtists.Count > 0 ? Join(details.AlbumArtists) : track.AlbumArtist);
        tags.Add("Info_Track", track.TrackNumber?.ToString(culture));
        tags.Add("Info_Disc", track.DiscNumber?.ToString(culture));
        tags.Add(
            details.ReleaseDate is null ? "Info_Year" : "Info_ReleaseDate",
            details.ReleaseDate?.ToString("d", culture) ?? track.Year?.ToString(culture));
        tags.Add("Info_Genres", Join(details.Genres));
        foreach (var role in details.People.GroupBy(p => p.Role).OrderBy(g => g.Key))
        {
            tags.Add($"InfoRole_{role.Key}", Join(role.Select(p => p.Name)));
        }

        tags.Add("Info_Label", Join(details.Studios));
        tags.Add("Info_Length", track.Duration is null ? null : Durations.Track(track.Duration));
        tags.Add("Info_TrackGain", Gain(track.NormalizationGain));
        tags.Add("Info_AlbumGain", Gain(track.AlbumNormalizationGain));
        tags.Add("Info_Lyrics", details.HasLyrics is { } hasLyrics ? YesNo(hasLyrics) : null);
        tags.Add("Info_ServerTags", Join(details.Tags));
        foreach (var (key, id) in details.ProviderIds)
        {
            tags.AddLabelled(
                ProviderLabels.TryGetValue(key, out var label) ? strings.GetString(label) : Format("Info_ProviderId", key),
                id);
        }

        var file = new Rows(strings);
        file.Add("Info_Format", Upper(details.Container));
        file.Add("Info_Codec", Upper(details.Codec));
        file.Add("Info_Bitrate", details.Bitrate is { } bitrate ? Format("Info_BitrateValue", Math.Round(bitrate / 1000d).ToString("N0", culture)) : null);
        file.Add("Info_SampleRate", details.SampleRate is { } rate ? Format("Info_SampleRateValue", (rate / 1000d).ToString("0.#", culture)) : null);
        file.Add("Info_BitDepth", details.BitDepth is { } depth ? Format("Info_BitDepthValue", depth.ToString(culture)) : null);
        file.Add("Info_Channels", details.Channels switch
        {
            null or <= 0 => null,
            1 => strings.GetString("Info_Mono"),
            2 => strings.GetString("Info_Stereo"),
            { } channels => Format("Info_ChannelCount", channels.ToString(culture)),
        });
        file.Add("Info_Size", details.Size is { } size ? ByteSizes.Format(size, strings) : null);
        file.Add("Info_Path", details.Path);

        var library = new Rows(strings);
        library.Add("Info_DateAdded", track.DateAdded?.ToLocalTime().ToString("g", culture));
        library.Add("Info_LastPlayed", track.LastPlayed?.ToLocalTime().ToString("g", culture));
        library.Add("Info_PlayCount", track.PlayCount.ToString(culture));
        library.Add("Info_Favourite", YesNo(track.IsFavorite));

        return
        [
            .. new[] { ("Info_SectionTags", tags), ("Info_SectionFile", file), ("Info_SectionLibrary", library) }
                .Where(s => s.Item2.Count > 0)
                .Select(s => new DetailSection(strings.GetString(s.Item1), s.Item2.ToList())),
        ];

        string? Gain(double? gain) => gain is { } value ? Format("Info_GainValue", value.ToString("+0.0;-0.0;0.0", culture)) : null;
    }

    private static string Join(IEnumerable<string> values) => string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)));

    /// <summary>"flac" → "FLAC"; a list such as "mov,mp4,m4a" gets spaces after its commas.</summary>
    private static string? Upper(string? value) =>
        value?.ToUpperInvariant().Replace(",", ", ", StringComparison.Ordinal);

    /// <summary>The rows of one section; empty values are skipped.</summary>
    private sealed class Rows(IStringResources strings) : List<KeyValuePair<string, string>>
    {
        public void Add(string labelKey, string? value) => AddLabelled(strings.GetString(labelKey), value);

        public void AddLabelled(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Add(new KeyValuePair<string, string>(label, value));
            }
        }
    }
}
