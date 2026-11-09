using System.Globalization;
using Groovefin.Core.Models;

namespace Groovefin.Core.ViewModels;

/// <summary>How search shows a song (#391): its artists, album and year, e.g. "Asian Dub Foundation · Rafi's Revenge · 1998".</summary>
public static class SearchTexts
{
    public static string TrackDetails(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return string.Join(
            " · ",
            new[]
            {
                string.Join(", ", track.Artists.Select(a => a.Name)),
                track.AlbumName ?? string.Empty,
                track.Year?.ToString(CultureInfo.CurrentCulture) ?? string.Empty,
            }.Where(s => s.Length > 0));
    }
}
