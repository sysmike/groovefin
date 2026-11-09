namespace Groovefin.Core.ViewModels;

/// <summary>
/// Jumping to a letter in a list sorted by name (#390): the letters offered, and where one starts. The server counts
/// the items that sort before the letter; that count is the index of the first item with it.
/// </summary>
public static class LetterJump
{
    /// <summary>The most items loaded with one request while a list loads up to a letter.</summary>
    public const int MaxPage = 1000;

    /// <summary>Gets the letters to jump to: "#" (digits and signs, the start of the list) and A to Z.</summary>
    public static IReadOnlyList<string> Letters { get; } = ["#", .. Enumerable.Range('A', 26).Select(c => ((char)c).ToString())];

    /// <summary>
    /// Gets the name the items must sort before to be counted, or <see langword="null"/> for "#", which is at the
    /// start. Jellyfin compares lower-case names.
    /// </summary>
    public static string? NameBefore(string letter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(letter);
        return letter == "#" ? null : letter.ToLowerInvariant();
    }

    /// <summary>
    /// Finds where <paramref name="letter"/> starts and loads the list up to there. <paramref name="countBefore"/>
    /// asks the server how many items sort before a name; <paramref name="loadMore"/> loads at least the given number
    /// of further items. Returns the index to show, or <see langword="null"/> when the list is empty.
    /// </summary>
    public static async Task<int?> LoadUpToAsync(
        string letter,
        Func<string, Task<int>> countBefore,
        Func<int> loaded,
        Func<int> total,
        Func<int, Task> loadMore)
    {
        ArgumentNullException.ThrowIfNull(countBefore);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(total);
        ArgumentNullException.ThrowIfNull(loadMore);

        var index = NameBefore(letter) is { } name ? await countBefore(name) : 0;
        while (loaded() <= index && loaded() < total())
        {
            var before = loaded();
            await loadMore(Math.Min(MaxPage, index - before + 1));
            if (loaded() == before)
            {
                // Nothing came: a failure (already shown) or the list was reloaded meanwhile.
                break;
            }
        }

        return loaded() == 0 ? null : Math.Min(index, loaded() - 1);
    }
}
