using System.Text.RegularExpressions;
using System.Xml.Linq;
using Groovefin.Core.Api;
using Groovefin.Core.Equalizer;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Queue;
using Groovefin.Core.Settings;
using Groovefin.Core.Shortcuts;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.Localization;

/// <summary>
/// Guards the app's string resources: en-US and de-DE carry the same keys and placeholders, every
/// <c>x:Uid</c> and every key the code asks for exists, and no string is left behind unused. The files are
/// read from the source tree, so this runs on any OS.
/// </summary>
public sealed partial class ResourceFileTests
{
    private static readonly string Root = FindRoot();
    private static readonly IReadOnlyList<KeyValuePair<string, string>> EnglishEntries = Load("en-US");
    private static readonly IReadOnlyList<KeyValuePair<string, string>> GermanEntries = Load("de-DE");
    private static readonly Dictionary<string, string> English = ToDictionary(EnglishEntries);
    private static readonly Dictionary<string, string> German = ToDictionary(GermanEntries);

    [Fact]
    public void No_key_is_defined_twice()
    {
        Duplicates(EnglishEntries).Should().BeEmpty("en-US");
        Duplicates(GermanEntries).Should().BeEmpty("de-DE");
    }

    [Fact]
    public void German_has_exactly_the_english_keys()
    {
        English.Keys.Except(German.Keys).Should().BeEmpty("every en-US string needs a de-DE translation");
        German.Keys.Except(English.Keys).Should().BeEmpty("de-DE must not keep strings en-US no longer has");
    }

    [Fact]
    public void No_string_is_empty()
    {
        English.Concat(German).Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Key).Should().BeEmpty();
    }

    [Fact]
    public void Translations_keep_the_placeholders()
    {
        English
            .Where(e => German.TryGetValue(e.Key, out var german) && !Placeholders(german).SequenceEqual(Placeholders(e.Value)))
            .Select(e => e.Key)
            .Should().BeEmpty("a translation must use the same {0}, {1}… as the English text");
    }

    [Fact]
    public void Access_keys_are_unique_and_short_in_each_language()
    {
        foreach (var strings in new[] { English, German })
        {
            var keys = strings.Where(e => e.Key.EndsWith("AccessKey", StringComparison.Ordinal)).ToList();

            keys.Should().NotBeEmpty();
            keys.Where(e => e.Value.Length is < 1 or > 2).Select(e => e.Key).Should().BeEmpty("access keys are one or two characters");
            keys.GroupBy(e => e.Value, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(e => e.Key))}")
                .Should().BeEmpty("all access keys share the window's scope, so each letter can be used once");
        }
    }

    [Fact]
    public void Every_xuid_has_strings_and_every_property_string_has_its_xuid()
    {
        var uids = Sources("*.xaml").SelectMany(text => UidPattern().Matches(text).Select(m => m.Groups[1].Value)).ToHashSet();
        var owners = English.Keys.Where(k => k.Contains('.', StringComparison.Ordinal)).Select(k => k[..k.IndexOf('.', StringComparison.Ordinal)]).ToHashSet();

        uids.Except(owners).Should().BeEmpty("an x:Uid without strings shows no text");
        owners.Except(uids).Should().BeEmpty("strings for an x:Uid that no longer exists are stale");
    }

    [Fact]
    public void Every_key_the_code_names_exists()
    {
        var named = Sources("*.cs").SelectMany(text => KeyLiteralPattern().Matches(text).Select(m => m.Groups[1].Value)).ToHashSet();

        named.Where(k => !English.ContainsKey(k)).Should().BeEmpty();
    }

    [Fact]
    public void Every_key_the_code_builds_exists()
    {
        string[] built =
        [
            .. Enum.GetValues<ShortcutAction>().Select(a => $"Shortcut_{a}"),
            .. KeyboardShortcuts.NamedKeys.Select(k => $"Key_{k}"),
            .. Enum.GetValues<SongColumn>().Select(c => $"SongColumn_{c}"),
            .. Enum.GetValues<HomeShelfKind>().Select(k => $"HomeShelf_{k}"),
            .. Enum.GetValues<RepeatMode>().Select(m => $"NowPlaying_Repeat{m}"),
            .. Enum.GetValues<AlbumSortField>().Select(f => $"AlbumSort_{f}"),
            .. Enum.GetValues<ArtistSortField>().Select(f => $"ArtistSort_{f}"),
            .. PageKeys.MainSections.Select(k => $"StartPage_{k}"),
            .. Enum.GetValues<EqualizerPreset>().Select(p => $"EqualizerPreset_{p}"),
            .. Enum.GetValues<PersonRole>().Select(r => $"InfoRole_{r}"),
        ];

        built.Where(k => !English.ContainsKey(k)).Should().BeEmpty();
    }

    [Fact]
    public void Every_plain_string_is_used()
    {
        var code = string.Join('\n', Sources("*.cs").Concat(Sources("*.appxmanifest")));
        var prefixes = BuiltKeyPattern().Matches(code).Select(m => m.Groups[1].Value).ToList();

        English.Keys
            .Where(k => !k.Contains('.', StringComparison.Ordinal))
            .Where(k => !code.Contains($"\"{k}\"", StringComparison.Ordinal)
                && !code.Contains($"ms-resource:{k}", StringComparison.Ordinal)
                && !prefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
            .Should().BeEmpty("strings nothing asks for are stale");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Groovefin.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The source tree (Groovefin.sln) was not found.");
    }

    private static List<KeyValuePair<string, string>> Load(string language) =>
    [
        .. XDocument.Load(Path.Join(Root, "src", "Groovefin.App", "Strings", language, "Resources.resw"))
            .Root!.Elements("data")
            .Select(d => KeyValuePair.Create((string)d.Attribute("name")!, (string?)d.Element("value") ?? string.Empty)),
    ];

    private static Dictionary<string, string> ToDictionary(IEnumerable<KeyValuePair<string, string>> entries) =>
        entries.GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

    private static IEnumerable<string> Duplicates(IEnumerable<KeyValuePair<string, string>> entries) =>
        entries.GroupBy(e => e.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);

    private static string[] Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal)];

    private static IEnumerable<string> Sources(string pattern) =>
        Directory.EnumerateFiles(Path.Join(Root, "src"), pattern, SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(File.ReadAllText);

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex("x:Uid=\"([^\"]+)\"")]
    private static partial Regex UidPattern();

    /// <summary>
    /// Resource keys are <c>Area_Name</c>, e.g. <c>"NowPlaying_Play"</c>. ALL_CAPS literals such as
    /// <c>"SET_ACTIVITY"</c> are protocol words, not keys.
    /// </summary>
    [GeneratedRegex("\"([A-Z][a-z][A-Za-z]*_[A-Za-z0-9]+)\"")]
    private static partial Regex KeyLiteralPattern();

    /// <summary>The fixed start of a key built at run time, e.g. <c>$"Shortcut_{action}"</c>.</summary>
    [GeneratedRegex("\\$\"([A-Z][A-Za-z]*_?[A-Za-z]*)\\{")]
    private static partial Regex BuiltKeyPattern();
}
