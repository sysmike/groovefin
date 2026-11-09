using System.Globalization;
using System.Text;
using Groovefin.Core.Api;
using Groovefin.Core.Models;

namespace Groovefin.Core.Search;

/// <summary>
/// Makes the server's search forgiving (#391). Identical recordings (same title, artists, album and length) come once.
/// When the server finds nothing, each word is searched on its own, and the first three letters of longer words
/// too; what comes back is kept when every word of the query matches a word of it, by its start or with a typo or
/// two. So "dub foundation asian" and "asain dub" both find Asian Dub Foundation.
/// </summary>
public sealed class ForgivingSearch(ISearchService inner) : ISearchService
{
    /// <summary>Words this long or longer are also searched by their first three letters, to get past a typo.</summary>
    public const int PrefixSearchLength = 5;

    /// <summary>Candidates asked for per word: more than shown, as only some match every word.</summary>
    public const int CandidatesPerWord = 20;

    public async Task<SearchResults> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var direct = Deduplicate(await inner.SearchAsync(query, limit, cancellationToken).ConfigureAwait(false));
        if (direct.Count > 0)
        {
            return direct;
        }

        var words = Words(query);
        List<string> terms =
        [
            .. words,
            .. words.Where(w => w.Length >= PrefixSearchLength).Select(w => w[..3]),
        ];
        terms = [.. terms.Distinct(StringComparer.Ordinal)];
        if (terms.Count == 0 || (terms.Count == 1 && string.Equals(terms[0], Fold(query.Trim()), StringComparison.Ordinal)))
        {
            // The same search again would find nothing again.
            return direct;
        }

        var candidates = await Task.WhenAll(terms.Select(t => inner.SearchAsync(t, Math.Max(limit, CandidatesPerWord), cancellationToken))).ConfigureAwait(false);
        return Deduplicate(new SearchResults(
            Rank(candidates.SelectMany(r => r.Artists), a => a.Id, a => [a.Name], words, limit),
            Rank(candidates.SelectMany(r => r.Albums), a => a.Id, a => [a.Name, a.AlbumArtist], words, limit),
            Rank(candidates.SelectMany(r => r.Tracks), t => t.Id, t => [t.Name, t.AlbumName, .. t.Artists.Select(a => a.Name)], words, limit),
            Rank(candidates.SelectMany(r => r.Playlists), p => p.Id, p => [p.Name], words, limit))
        {
            Genres = Rank(candidates.SelectMany(r => r.Genres), g => g.Id, g => [g.Name], words, limit),
        });
    }

    /// <summary>The same recording found more than once (on a compilation and its album, say, with the same name): once.</summary>
    internal static SearchResults Deduplicate(SearchResults results)
    {
        var tracks = results.Tracks
            .DistinctBy(t => (Fold(t.Name), Fold(t.AlbumName ?? string.Empty), string.Join('|', t.Artists.Select(a => Fold(a.Name))), (long)(t.Duration?.TotalSeconds ?? -1)))
            .ToList();
        return tracks.Count == results.Tracks.Count ? results : results with { Tracks = tracks };
    }

    /// <summary>
    /// Whether every query word matches a word of <paramref name="texts"/>: it starts the word, or is that close to
    /// its start (one letter off from 4 letters, two from 7). Returns how many matched exactly, or -1.
    /// </summary>
    internal static int Score(IReadOnlyList<string> words, IEnumerable<string?> texts)
    {
        var tokens = texts.Where(t => !string.IsNullOrWhiteSpace(t)).SelectMany(t => Words(t!)).ToList();
        var exact = 0;
        foreach (var word in words)
        {
            if (tokens.Any(t => t.StartsWith(word, StringComparison.Ordinal)))
            {
                exact++;
                continue;
            }

            var allowed = word.Length >= 7 ? 2 : word.Length >= 4 ? 1 : 0;
            if (allowed == 0 || !tokens.Any(t => Distance(word, t.Length > word.Length ? t[..word.Length] : t) <= allowed))
            {
                return -1;
            }
        }

        return exact;
    }

    /// <summary>The words of a text, lower case and without accents.</summary>
    internal static List<string> Words(string text) =>
        [.. Fold(text).Split((char[])[' ', '\t', ',', '.', '-', '(', ')', '/', '&', '\'', '"', ':', ';', '!', '?'], StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>The optimal string alignment distance: insertions, deletions, substitutions and swapped neighbours.</summary>
    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }

    private static List<T> Rank<T>(IEnumerable<T> items, Func<T, string> id, Func<T, IEnumerable<string?>> texts, IReadOnlyList<string> words, int limit) =>
        [
            .. items
                .DistinctBy(id)
                .Select((item, order) => (Item: item, Score: Score(words, texts(item)), Order: order))
                .Where(c => c.Score >= 0)
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Order)
                .Take(limit)
                .Select(c => c.Item),
        ];

    /// <summary>Lower case, without accents: "Café" and "cafe" are the same word.</summary>
    private static string Fold(string text)
    {
        var decomposed = text.ToLower(CultureInfo.CurrentCulture).Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(c);
            }
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }
}
