using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Formatting;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>One row of the songs table.</summary>
public sealed partial class SongRow(Track track) : ObservableObject, ICurrentTrackRow
{
    public Track Track { get; } = track;

    /// <summary>Gets or sets whether the row's track is playing (#247).</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>Gets the cell text of <paramref name="column"/>.</summary>
    public string Text(SongColumn column) => column switch
    {
        SongColumn.TrackNumber => Track.TrackNumber?.ToString(CultureInfo.CurrentCulture) ?? string.Empty,
        SongColumn.Title => Track.Name,
        SongColumn.Artist => string.Join(", ", Track.Artists.Select(a => a.Name)),
        SongColumn.Album => Track.AlbumName ?? string.Empty,
        SongColumn.AlbumArtist => Track.AlbumArtist ?? string.Empty,
        SongColumn.Year => Track.Year?.ToString(CultureInfo.CurrentCulture) ?? string.Empty,
        SongColumn.Duration => Durations.Track(Track.Duration),
        SongColumn.PlayCount => Track.PlayCount.ToString(CultureInfo.CurrentCulture),
        SongColumn.DateAdded => Track.DateAdded?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? string.Empty,
        _ => string.Empty,
    };

    /// <summary>Gets the text Narrator reads for the row: title, artists, album and length.</summary>
    public string AccessibleName =>
        string.Join(", ", new[] { SongColumn.Title, SongColumn.Artist, SongColumn.Album, SongColumn.Duration }.Select(Text).Where(s => s.Length > 0));
}

/// <summary>One column of the songs table: header, width, visibility and sort state.</summary>
public sealed partial class SongColumnViewModel(SongColumn column, string header, TrackSortField? sortField) : ObservableObject
{
    public SongColumn Column { get; } = column;

    public string Header { get; } = header;

    /// <summary>Gets the sort order of the column, or <see langword="null"/> when it cannot be sorted.</summary>
    public TrackSortField? SortField { get; } = sortField;

    /// <summary>Gets a value indicating whether the column can be hidden; the title always shows.</summary>
    public bool CanHide => Column != SongColumn.Title;

    /// <summary>Gets a value indicating whether the column holds numbers, which are right-aligned.</summary>
    public bool IsNumeric => Column is SongColumn.TrackNumber or SongColumn.Year or SongColumn.Duration or SongColumn.PlayCount;

    [ObservableProperty]
    public partial double Width { get; internal set; }

    [ObservableProperty]
    public partial bool IsVisible { get; internal set; }

    /// <summary>Gets the sort direction when the table is sorted by this column, otherwise <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial bool? SortDescending { get; internal set; }
}

/// <summary>
/// Songs page: every track of the library in a table, loaded page by page as the view scrolls
/// (<see cref="LoadMoreCommand"/>). Clicking a header sorts by it; columns can be shown, hidden, resized
/// and moved, and the layout and sort order are remembered in settings.
/// </summary>
public sealed partial class SongsViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 200;

    public const double MinColumnWidth = 40;

    public const double MaxColumnWidth = 800;

    /// <summary>How much the keyboard and menu commands widen or narrow a column.</summary>
    public const double WidthStep = 24;

    private static readonly (SongColumn Column, double Width, bool Visible)[] DefaultLayout =
    [
        (SongColumn.TrackNumber, 48, true),
        (SongColumn.Title, 280, true),
        (SongColumn.Artist, 200, true),
        (SongColumn.Album, 200, true),
        (SongColumn.AlbumArtist, 180, false),
        (SongColumn.Year, 64, true),
        (SongColumn.Duration, 72, true),
        (SongColumn.PlayCount, 72, false),
        (SongColumn.DateAdded, 112, false),
    ];

    private readonly ILibraryService _library;
    private readonly ISettingsService _settings;
    private readonly IPlayer _player;
    private readonly IErrorReporter _errors;
    private readonly IStringResources _strings;
    private CancellationTokenSource _loadCancellation = new();
    private bool _disposed;
    private int _generation;
    private bool _initialized;

    public SongsViewModel(ILibraryService library, ISettingsService settings, IPlayer player, IErrorReporter errors, IStringResources strings, CurrentTrack currentTrack)
    {
        currentTrack.Watch(Songs);
        _library = library;
        _settings = settings;
        _player = player;
        _errors = errors;
        _strings = strings;

        var browse = settings.Current.Browse;
        SortBy = browse.SongSort;
        Descending = browse.SongSortDescending;
        ApplyLayout(browse.SongColumns);
        UpdateSortState();
    }

    /// <summary>Raised when a column is shown, hidden, resized or moved, so the table can lay itself out again.</summary>
    public event EventHandler? LayoutChanged;

    public ObservableCollection<SongRow> Songs { get; } = [];

    /// <summary>Gets every column in display order, hidden ones included.</summary>
    public ObservableCollection<SongColumnViewModel> Columns { get; } = [];

    /// <summary>Gets the shown columns in display order.</summary>
    public IReadOnlyList<SongColumnViewModel> VisibleColumns => [.. Columns.Where(c => c.IsVisible)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial int TotalCount { get; private set; }

    /// <summary>Gets a value indicating whether loading finished and the library has no songs.</summary>
    public bool IsEmpty => _initialized && !IsLoading && Songs.Count == 0;

    [ObservableProperty]
    public partial TrackSortField SortBy { get; private set; }

    [ObservableProperty]
    public partial bool Descending { get; private set; }

    /// <summary>Loads the first page. Called once when the page is shown.</summary>
    public Task InitializeAsync()
    {
        _initialized = true;
        return ReloadAsync();
    }

    /// <summary>Starts over: cancels a running load, clears the table and loads the first page.</summary>
    public Task ReloadAsync()
    {
        if (!_initialized)
        {
            return Task.CompletedTask;
        }

        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var generation = ++_generation;

        Songs.Clear();
        TotalCount = 0;
        IsLoading = false;
        return LoadPageAsync(generation);
    }

    /// <summary>Cancels loading. Safe to call twice: the page and, at shutdown, the container dispose it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
    }

    private bool CanLoadMore() => !IsLoading && Songs.Count < TotalCount;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync() => LoadPageAsync(_generation);

    /// <summary>Plays the loaded songs from <paramref name="row"/> on.</summary>
    [RelayCommand]
    private void Play(SongRow row)
    {
        var index = Songs.IndexOf(row);
        if (index >= 0)
        {
            _player.Play([.. Songs.Select(s => s.Track)], index);
        }
    }

    /// <summary>Sorts by the column; a second click on the sorted column reverses the order.</summary>
    [RelayCommand]
    private void Sort(SongColumnViewModel column)
    {
        if (column.SortField is not { } field)
        {
            return;
        }

        if (field == SortBy)
        {
            Descending = !Descending;
        }
        else
        {
            SortBy = field;
            Descending = false;
        }

        _settings.Update(s => s with { Browse = s.Browse with { SongSort = SortBy, SongSortDescending = Descending } });
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void ToggleColumn(SongColumnViewModel column)
    {
        if (column.CanHide)
        {
            column.IsVisible = !column.IsVisible;
            CommitLayout();
        }
    }

    [RelayCommand]
    private void MoveColumnLeft(SongColumnViewModel column) => MoveVisible(column, -1);

    [RelayCommand]
    private void MoveColumnRight(SongColumnViewModel column) => MoveVisible(column, +1);

    [RelayCommand]
    private void WidenColumn(SongColumnViewModel column)
    {
        ResizeColumn(column, column.Width + WidthStep);
        CommitLayout();
    }

    [RelayCommand]
    private void NarrowColumn(SongColumnViewModel column)
    {
        ResizeColumn(column, column.Width - WidthStep);
        CommitLayout();
    }

    [RelayCommand]
    private void ResetColumns()
    {
        ApplyLayout([]);
        UpdateSortState();
        CommitLayout();
    }

    /// <summary>Sets a column's width while the user drags its edge; <see cref="CommitLayout"/> saves it.</summary>
    public void ResizeColumn(SongColumnViewModel column, double width)
    {
        ArgumentNullException.ThrowIfNull(column);
        // Whole pixels: smaller changes are not worth a layout pass.
        var clamped = Math.Clamp(Math.Round(width), MinColumnWidth, MaxColumnWidth);
        if (Math.Abs(clamped - column.Width) >= 1)
        {
            column.Width = clamped;
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Moves <paramref name="column"/> to <paramref name="index"/> in <see cref="Columns"/> and saves the layout.</summary>
    public void MoveColumn(SongColumnViewModel column, int index)
    {
        ArgumentNullException.ThrowIfNull(column);
        var from = Columns.IndexOf(column);
        var to = Math.Clamp(index, 0, Columns.Count - 1);
        if (from >= 0 && from != to)
        {
            Columns.Move(from, to);
            CommitLayout();
        }
    }

    /// <summary>Raises <see cref="LayoutChanged"/> and saves the column layout.</summary>
    public void CommitLayout()
    {
        OnPropertyChanged(nameof(VisibleColumns));
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        List<SongColumnSettings> layout = [.. Columns.Select(c => new SongColumnSettings { Column = c.Column, Width = c.Width, IsVisible = c.IsVisible })];
        _settings.Update(s => s with { Browse = s.Browse with { SongColumns = layout } });
    }

    /// <summary>Swaps the column with its visible neighbour, so hidden columns are skipped.</summary>
    private void MoveVisible(SongColumnViewModel column, int direction)
    {
        var visible = VisibleColumns;
        var position = visible.ToList().IndexOf(column);
        var neighbour = position + direction;
        if (position < 0 || neighbour < 0 || neighbour >= visible.Count)
        {
            return;
        }

        MoveColumn(column, Columns.IndexOf(visible[neighbour]));
    }

    /// <summary>
    /// Fills <see cref="Columns"/> from saved settings: unknown and repeated columns are dropped, missing
    /// ones are added in their default place, widths are kept within bounds and the title always shows.
    /// </summary>
    private void ApplyLayout(IReadOnlyList<SongColumnSettings> saved)
    {
        var defaults = DefaultLayout.ToDictionary(d => d.Column);
        var layout = saved
            .Where(c => defaults.ContainsKey(c.Column))
            .DistinctBy(c => c.Column)
            .Select(c => (c.Column, c.Width, c.IsVisible))
            .ToList();
        foreach (var (column, width, visible) in DefaultLayout)
        {
            if (!layout.Exists(c => c.Column == column))
            {
                var after = layout.FindIndex(c => c.Column == DefaultBefore(column));
                layout.Insert(after + 1, (column, width, visible));
            }
        }

        Columns.Clear();
        foreach (var (column, width, visible) in layout)
        {
            Columns.Add(new SongColumnViewModel(column, _strings.GetString($"SongColumn_{column}"), SortFieldOf(column))
            {
                Width = double.IsFinite(width) ? Math.Clamp(width, MinColumnWidth, MaxColumnWidth) : defaults[column].Width,
                IsVisible = visible || column == SongColumn.Title,
            });
        }
    }

    /// <summary>The column right before <paramref name="column"/> in the default layout.</summary>
    private static SongColumn? DefaultBefore(SongColumn column)
    {
        var index = Array.FindIndex(DefaultLayout, d => d.Column == column);
        return index > 0 ? DefaultLayout[index - 1].Column : null;
    }

    private static TrackSortField? SortFieldOf(SongColumn column) => column switch
    {
        SongColumn.Title => TrackSortField.Name,
        SongColumn.Artist => TrackSortField.Artist,
        SongColumn.Album => TrackSortField.Album,
        SongColumn.AlbumArtist => TrackSortField.AlbumArtist,
        SongColumn.Year => TrackSortField.Year,
        SongColumn.Duration => TrackSortField.Duration,
        SongColumn.PlayCount => TrackSortField.PlayCount,
        SongColumn.DateAdded => TrackSortField.DateAdded,
        _ => null,
    };

    // Before PropertyChanged is raised, so listeners see the columns' new sort state.
    partial void OnSortByChanged(TrackSortField value) => UpdateSortState();

    partial void OnDescendingChanged(bool value) => UpdateSortState();

    private void UpdateSortState()
    {
        foreach (var column in Columns)
        {
            column.SortDescending = column.SortField == SortBy ? Descending : null;
        }
    }

    private async Task LoadPageAsync(int generation)
    {
        var cancellation = _loadCancellation.Token;
        IsLoading = true;
        try
        {
            var query = new TrackQuery { StartIndex = Songs.Count, Limit = PageSize, SortBy = SortBy, Descending = Descending };
            var page = await _library.GetTracksAsync(query, cancellation);

            // A newer reload started while this page was loading: drop the stale result.
            if (generation != _generation)
            {
                return;
            }

            foreach (var track in page.Items)
            {
                Songs.Add(new SongRow(track));
            }

            TotalCount = page.TotalCount;
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            if (generation == _generation)
            {
                _errors.Report(ex, Operations.LoadSongs);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a reload or the page was closed.
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }
}
