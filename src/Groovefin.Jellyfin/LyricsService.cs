using System.Net;
using Groovefin.Core.Api;
using Groovefin.Core.Lyrics;
using Jellyfin.Sdk.Generated.Models;
using LyricLine = Groovefin.Core.Lyrics.LyricLine;

namespace Groovefin.Jellyfin;

/// <summary><see cref="ILyricsService"/> on Jellyfin's <c>/Audio/{id}/Lyrics</c> for the active connection.</summary>
/// <remarks>
/// The server parses LRC files itself and sends timed lines. Lyrics stored without parsing (one block of
/// LRC text, as some taggers embed it) are parsed here.
/// </remarks>
internal sealed class LyricsService(JellyfinClient client) : ILyricsService
{
    public async Task<TrackLyrics?> GetLyricsAsync(string trackId, CancellationToken cancellationToken = default)
    {
        var id = Guid.TryParse(trackId, out var guid) ? guid : throw new ArgumentException($"'{trackId}' is not a Jellyfin item ID.", nameof(trackId));
        var (api, _) = client.Active;

        LyricDto? dto;
        try
        {
            dto = await JellyfinCall.RunAsync(
                () => api.Audio[id].Lyrics.GetAsync(cancellationToken: cancellationToken),
                "Loading lyrics").ConfigureAwait(false);
        }
        catch (JellyfinException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var lines = dto?.Lyrics?.Where(l => l is not null).ToList() ?? [];
        if (lines.Count == 0)
        {
            return null;
        }

        if (lines.All(l => l.Start is not null))
        {
            return new TrackLyrics([.. lines.Select(l => new LyricLine(TimeSpan.FromTicks(l.Start!.Value), l.Text ?? string.Empty))], Metadata(dto!.Metadata));
        }

        var text = string.Join('\n', lines.Select(l => l.Text ?? string.Empty));
        var parsed = LrcParser.Parse(text);
        return parsed.Lines.Count == 0 ? null : parsed with { Metadata = Merge(parsed.Metadata, Metadata(dto!.Metadata)) };
    }

    /// <summary>The server's metadata; its offset is informational, the line times are used as sent.</summary>
    private static LyricsMetadata Metadata(LyricMetadata? metadata) => new()
    {
        Title = metadata?.Title,
        Artist = metadata?.Artist,
        Album = metadata?.Album,
        By = metadata?.By,
        Length = metadata?.Length is { } length ? TimeSpan.FromTicks(length) : null,
        Offset = metadata?.Offset is { } offset ? TimeSpan.FromTicks(offset) : TimeSpan.Zero,
    };

    /// <summary>Tags from the parsed text win; the server fills the gaps.</summary>
    private static LyricsMetadata Merge(LyricsMetadata parsed, LyricsMetadata server) => parsed with
    {
        Title = parsed.Title ?? server.Title,
        Artist = parsed.Artist ?? server.Artist,
        Album = parsed.Album ?? server.Album,
        By = parsed.By ?? server.By,
        Length = parsed.Length ?? server.Length,
    };
}
