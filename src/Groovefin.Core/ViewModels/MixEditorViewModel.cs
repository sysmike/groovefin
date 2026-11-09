using CommunityToolkit.Mvvm.ComponentModel;
using Groovefin.Core.Mixes;
using Groovefin.Core.Models;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// The rules of a new or changed mix (#401), as the editor dialog shows them: a name, a genre, years, favourites only,
/// presets for plays, the last play and the date added, the order and the size. <see cref="Build"/> gives the rules.
/// </summary>
public sealed partial class MixEditorViewModel : ObservableObject
{
    /// <summary>The earliest and latest year the year boxes take.</summary>
    public const int MinYearValue = 1000;

    public const int MaxYearValue = 2999;

    private readonly IReadOnlyList<Genre> _genres;

    /// <summary>A genre the server no longer lists: kept unless another one is chosen.</summary>
    private readonly (string Id, string? Name)? _keptGenre;

    public MixEditorViewModel(IStringResources strings, IReadOnlyList<Genre> genres, MixRules? existing = null)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(genres);
        _genres = [.. genres.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)];
        Title = strings.GetString(existing is null ? "MixEditor_NewTitle" : "MixEditor_EditTitle");
        GenreOptions = [strings.GetString("MixEditor_AnyGenre"), .. _genres.Select(g => g.Name)];
        PlaysOptions = [.. MixEditorPresets.Plays.Keys.Select(strings.GetString)];
        NotPlayedOptions = [.. MixEditorPresets.NotPlayed.Keys.Select(strings.GetString)];
        AddedOptions = [.. MixEditorPresets.Added.Keys.Select(strings.GetString)];
        OrderOptions = [.. MixEditorPresets.OrderKeys.Select(strings.GetString)];
        SizeOptions = [.. MixEditorPresets.Sizes.Select(size => strings.GetString("Mix_Size").Replace("{0}", size.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal))];

        var rules = existing ?? new MixRules();
        Name = rules.Name;
        GenreIndex = rules.GenreId is { } id ? Math.Max(0, _genres.ToList().FindIndex(g => g.Id == id) + 1) : 0;
        _keptGenre = rules.GenreId is not null && GenreIndex == 0 ? (rules.GenreId, rules.GenreName) : null;
        FromYear = rules.FromYear ?? double.NaN;
        ToYear = rules.ToYear ?? double.NaN;
        FavouritesOnly = rules.FavouritesOnly;
        PlaysIndex = Math.Max(0, MixEditorPresets.Plays.IndexOf(rules.MinPlays, rules.MaxPlays));
        NotPlayedIndex = Math.Max(0, MixEditorPresets.NotPlayed.IndexOf(rules.NotPlayedForDays));
        AddedIndex = Math.Max(0, MixEditorPresets.Added.IndexOf(rules.AddedWithinDays));
        OrderIndex = (int)rules.Order;
        SizeIndex = Math.Max(0, MixEditorPresets.Sizes.ToList().IndexOf(rules.Size));
        if (existing is null)
        {
            SizeIndex = MixEditorPresets.Sizes.ToList().IndexOf(new MixRules().Size);
        }
    }

    public string Title { get; }

    public IReadOnlyList<string> GenreOptions { get; }

    public IReadOnlyList<string> PlaysOptions { get; }

    public IReadOnlyList<string> NotPlayedOptions { get; }

    public IReadOnlyList<string> AddedOptions { get; }

    public IReadOnlyList<string> OrderOptions { get; }

    public IReadOnlyList<string> SizeOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the genre as an index into <see cref="GenreOptions"/>; 0 is every genre.</summary>
    [ObservableProperty]
    public partial int GenreIndex { get; set; }

    /// <summary>Gets or sets the first year, or <see cref="double.NaN"/> when empty (as a number box gives it).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial double FromYear { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial double ToYear { get; set; }

    [ObservableProperty]
    public partial bool FavouritesOnly { get; set; }

    [ObservableProperty]
    public partial int PlaysIndex { get; set; }

    [ObservableProperty]
    public partial int NotPlayedIndex { get; set; }

    [ObservableProperty]
    public partial int AddedIndex { get; set; }

    [ObservableProperty]
    public partial int OrderIndex { get; set; }

    [ObservableProperty]
    public partial int SizeIndex { get; set; }

    /// <summary>Gets a value indicating whether the mix has a name and the years make sense.</summary>
    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Name)
        && YearOrEmpty(FromYear, out var from)
        && YearOrEmpty(ToYear, out var to)
        && (from is null || to is null || from <= to);

    /// <summary>The rules as chosen.</summary>
    /// <exception cref="InvalidOperationException">The choices can't be saved (<see cref="CanSave"/>).</exception>
    public MixRules Build()
    {
        if (!CanSave)
        {
            throw new InvalidOperationException("The mix needs a name and a valid year range.");
        }

        _ = YearOrEmpty(FromYear, out var from);
        _ = YearOrEmpty(ToYear, out var to);
        var genre = GenreIndex > 0 && GenreIndex <= _genres.Count ? _genres[GenreIndex - 1] : null;
        var plays = MixEditorPresets.Plays.Values[Math.Clamp(PlaysIndex, 0, MixEditorPresets.Plays.Values.Count - 1)];
        return new MixRules
        {
            Name = Name.Trim(),
            GenreId = genre?.Id ?? _keptGenre?.Id,
            GenreName = genre?.Name ?? _keptGenre?.Name,
            FromYear = from,
            ToYear = to,
            FavouritesOnly = FavouritesOnly,
            MinPlays = plays.Min,
            MaxPlays = plays.Max,
            NotPlayedForDays = MixEditorPresets.NotPlayed.Values[Math.Clamp(NotPlayedIndex, 0, MixEditorPresets.NotPlayed.Values.Count - 1)],
            AddedWithinDays = MixEditorPresets.Added.Values[Math.Clamp(AddedIndex, 0, MixEditorPresets.Added.Values.Count - 1)],
            Order = (MixOrder)Math.Clamp(OrderIndex, 0, MixEditorPresets.OrderKeys.Count - 1),
            Size = MixEditorPresets.Sizes[Math.Clamp(SizeIndex, 0, MixEditorPresets.Sizes.Count - 1)],
        };
    }

    /// <summary>An empty box, or a whole year in range.</summary>
    private static bool YearOrEmpty(double value, out int? year)
    {
        year = null;
        if (double.IsNaN(value))
        {
            return true;
        }

        if (value < MinYearValue || value > MaxYearValue || value != Math.Floor(value))
        {
            return false;
        }

        year = (int)value;
        return true;
    }
}
