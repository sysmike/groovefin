using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Formatting;
using Groovefin.Core.Images;
using Groovefin.Core.Layout;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Statistics;

namespace Groovefin.Core.ViewModels;

/// <summary>A bar of a statistics chart (#382): its height in pixels, and what its tooltip says.</summary>
public sealed record ChartBar(double Height, string ToolTip);

/// <summary>
/// A row of album or artist tiles on the statistics page (#382): as many as fit in the page's width, like the start
/// page's rows (#376), or all of them once "Show all" was chosen.
/// </summary>
public sealed partial class TileRow(string showAllText, string showLessText) : ObservableObject
{
    private List<object> _loaded = [];
    private int _columns = int.MaxValue;

    /// <summary>Gets the tiles shown.</summary>
    public ObservableCollection<object> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleText))]
    public partial bool IsExpanded { get; private set; }

    /// <summary>Gets a value indicating whether there are more tiles than fit in one row: "Show all" is shown.</summary>
    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    [ObservableProperty]
    public partial bool HasItems { get; private set; }

    /// <summary>Gets the toggle's text: "Show all", or "Show less" while all are shown.</summary>
    public string ToggleText => IsExpanded ? showLessText : showAllText;

    /// <summary>Gets how many tiles of the statistics page fit in <paramref name="width"/>.</summary>
    public static int TilesThatFit(double width) => TileGrid.Columns(width, StatisticsViewModel.TileWidth, StatisticsViewModel.TileSpacing);

    /// <summary>The row is <paramref name="columns"/> tiles wide now.</summary>
    public void Fit(int columns)
    {
        _columns = Math.Max(1, columns);
        Show();
    }

    internal void Replace(IEnumerable<object> items)
    {
        _loaded = [.. items];
        IsExpanded = false;
        Items.Clear();
        Show();
    }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        Show();
    }

    /// <summary>Adds or removes tiles at the end only, so the ones that stay are not built again.</summary>
    private void Show()
    {
        var count = IsExpanded ? _loaded.Count : Math.Min(_columns, _loaded.Count);
        while (Items.Count > count)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        while (Items.Count < count)
        {
            Items.Add(_loaded[Items.Count]);
        }

        HasItems = _loaded.Count > 0;
        HasMore = _loaded.Count > _columns;
    }
}

/// <summary>
/// The statistics page (#382): a week, month or year from the play history, or all time from the server's play
/// counts. It shows the listening time, plays, songs and artists (with the change from the period before, when the
/// history covers it), the plays per day or month and per hour, and the top artists, albums and songs. The top songs
/// play, shuffle and save as a playlist like On repeat.
/// </summary>
public sealed partial class StatisticsViewModel : ObservableObject
{
    /// <summary>The smallest tile width; the tiles grow to fill the row.</summary>
    public const double TileWidth = 152;

    public const double TileSpacing = 16;

    /// <summary>The height of the charts' bars at the most plays.</summary>
    public const double ChartHeight = 96;

    /// <summary>The top songs shown until "Show all".</summary>
    public const int SongsShown = 5;

    private readonly IListeningStatistics _statistics;
    private readonly ILibraryService _library;
    private readonly IPlaylistService _playlists;
    private readonly IPlayer _player;
    private readonly INavigationService _navigation;
    private readonly IErrorReporter _errors;
    private readonly IStringResources _strings;
    private readonly FavouritesService _favourites;
    private readonly IMessenger _messenger;
    private List<OnRepeatItem> _songs = [];
    private CancellationTokenSource? _load;
    private int _generation;
    private ListeningReport? _report;

    public StatisticsViewModel(
        IListeningStatistics statistics,
        ILibraryService library,
        IPlaylistService playlists,
        IPlayer player,
        INavigationService navigation,
        IErrorReporter errors,
        IStringResources strings,
        FavouritesService favourites,
        IMessenger messenger,
        CurrentTrack currentTrack)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(currentTrack);
        _statistics = statistics;
        _library = library;
        _playlists = playlists;
        _player = player;
        _navigation = navigation;
        _errors = errors;
        _strings = strings;
        _favourites = favourites;
        _messenger = messenger;
        TopSongs = currentTrack.Watch(new ObservableCollection<OnRepeatItem>());
        TopArtists = new TileRow(strings.GetString("Statistics_ShowAll"), strings.GetString("Statistics_ShowLess"));
        TopAlbums = new TileRow(strings.GetString("Statistics_ShowAll"), strings.GetString("Statistics_ShowLess"));
        Period = StatisticsPeriod.Containing(StatisticsRange.Month, statistics.Today, FirstDayOfWeek);
    }

    /// <summary>Gets or sets the scale of the display, for the size of the images asked for.</summary>
    public double DisplayScale { get; set; } = 1;

    /// <summary>Gets the period shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodName), nameof(Range), nameof(IsAllTime))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPeriodCommand), nameof(NextPeriodCommand))]
    public partial StatisticsPeriod Period { get; private set; } = StatisticsPeriod.AllTime;

    public StatisticsRange Range => Period.Range;

    public bool IsAllTime => Period.IsAllTime;

    /// <summary>Gets the period's name: "November 2026", "2026", "All time".</summary>
    public string PeriodName => Period.IsAllTime ? _strings.GetString("Statistics_AllTime") : Period.Name(CultureInfo.CurrentCulture);

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    /// <summary>Gets a value indicating whether the period has plays: the numbers, charts and lists are shown.</summary>
    [ObservableProperty]
    public partial bool HasPlays { get; private set; }

    /// <summary>Gets a value indicating whether the period loaded and has no plays.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    [ObservableProperty]
    public partial string ListeningTime { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Plays { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Songs { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Artists { get; private set; } = string.Empty;

    /// <summary>Gets the change in listening time from the period before, e.g. "+12% compared with October 2026"; empty without one.</summary>
    [ObservableProperty]
    public partial string ListeningTimeChange { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PlaysChange { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SongsChange { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ArtistsChange { get; private set; } = string.Empty;

    /// <summary>
    /// Gets what the numbers cover when it is not the whole period: "Since 9 November 2025" when the history began
    /// in it; for all time, since when the charts count. Empty otherwise.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCoverage))]
    public partial string Coverage { get; private set; } = string.Empty;

    public bool HasCoverage => Coverage.Length > 0;

    /// <summary>Gets the plays per day (a week or a month) or per month (a year, all time).</summary>
    public ObservableCollection<ChartBar> Activity { get; } = [];

    /// <summary>Gets the activity chart's heading line, e.g. "Most on November 8: 31 plays".</summary>
    [ObservableProperty]
    public partial string ActivitySummary { get; private set; } = string.Empty;

    /// <summary>Gets the name of the activity chart's first bar, under its start.</summary>
    [ObservableProperty]
    public partial string ActivityStart { get; private set; } = string.Empty;

    /// <summary>Gets the name of the activity chart's last bar, under its end.</summary>
    [ObservableProperty]
    public partial string ActivityEnd { get; private set; } = string.Empty;

    /// <summary>Gets the plays per hour of the day, 24 bars.</summary>
    public ObservableCollection<ChartBar> Hours { get; } = [];

    /// <summary>Gets the listening clock's heading line, e.g. "Most at 9:00 PM".</summary>
    [ObservableProperty]
    public partial string HoursSummary { get; private set; } = string.Empty;

    public TileRow TopArtists { get; }

    public TileRow TopAlbums { get; }

    /// <summary>Gets the top songs shown: the first <see cref="SongsShown"/>, or all of them after "Show all".</summary>
    public ObservableCollection<OnRepeatItem> TopSongs { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlaySongsCommand), nameof(ShuffleSongsCommand), nameof(SaveSongsAsPlaylistCommand))]
    public partial bool HasSongs { get; private set; }

    /// <summary>Gets a value indicating whether there are more top songs than <see cref="SongsShown"/>.</summary>
    [ObservableProperty]
    public partial bool HasMoreSongs { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SongsToggleText))]
    public partial bool SongsExpanded { get; private set; }

    public string SongsToggleText => _strings.GetString(SongsExpanded ? "Statistics_ShowLess" : "Statistics_ShowAll");

    private static DayOfWeek FirstDayOfWeek => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    /// <summary>Opening the page: reads the plays from other devices first, then shows the period.</summary>
    public async Task LoadAsync()
    {
        // Weak: the page's view model needs no unregistering.
        if (!_messenger.IsRegistered<FavouriteChangedMessage>(this))
        {
            _messenger.Register<StatisticsViewModel, FavouriteChangedMessage>(this, static (r, m) => r.OnFavouriteChanged(m));
        }

        IsLoading = true;
        try
        {
            await _statistics.SyncAsync();
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            // The plays from other devices come with the next sync; the page shows what it has.
        }

        await ShowAsync();
    }

    /// <summary>Shows the period of <paramref name="range"/> that holds today, as the segmented control asks.</summary>
    public Task SelectRangeAsync(StatisticsRange range)
    {
        if (range == Period.Range)
        {
            return Task.CompletedTask;
        }

        Period = StatisticsPeriod.Containing(range, _statistics.Today, FirstDayOfWeek);
        return ShowAsync();
    }

    /// <summary>Stops a load still running: the page was left.</summary>
    public void Cancel() => _load?.Cancel();

    private bool CanGoToPreviousPeriod() => !Period.IsAllTime && _report is { } report && report.HistoryStart < Period.Start;

    private bool CanGoToNextPeriod() => !Period.IsAllTime && Period.End <= _statistics.Today;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPeriod))]
    private Task PreviousPeriodAsync()
    {
        Period = Period.Previous;
        return ShowAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPeriod))]
    private Task NextPeriodAsync()
    {
        Period = Period.Next;
        return ShowAsync();
    }

    private IReadOnlyList<Track> SongTracks => [.. _songs.Select(s => s.Track)];

    [RelayCommand(CanExecute = nameof(HasSongs))]
    private void PlaySongs() => _player.Play(SongTracks);

    [RelayCommand(CanExecute = nameof(HasSongs))]
    private void ShuffleSongs() => _player.Shuffle(SongTracks);

    /// <summary>Plays the top songs from <paramref name="item"/> on (a row was clicked).</summary>
    [RelayCommand]
    private void PlaySong(OnRepeatItem item) => _player.Play(SongTracks, Math.Max(0, _songs.IndexOf(item)));

    /// <summary>Saves the top songs as a playlist named after the period, then opens it.</summary>
    [RelayCommand(CanExecute = nameof(HasSongs))]
    private async Task SaveSongsAsPlaylistAsync()
    {
        var name = _strings.GetString("Statistics_PlaylistName").Replace("{0}", PeriodName, StringComparison.Ordinal);
        try
        {
            var id = await _playlists.CreateAsync(name, [.. _songs.Select(s => s.Track.Id)]);
            _navigation.NavigateTo(PageKeys.Playlist, id);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            _errors.Report(ex, Operations.SavePlaylist);
        }
    }

    [RelayCommand]
    private void ToggleSongs()
    {
        SongsExpanded = !SongsExpanded;
        ShowSongs();
    }

    /// <summary>Toggles a song's heart, optimistic like On repeat's.</summary>
    [RelayCommand]
    private async Task ToggleSongFavouriteAsync(OnRepeatItem item)
    {
        var target = !item.IsFavourite;
        item.IsFavourite = target;
        if (!await _favourites.SetAsync(item.Track.Id, target))
        {
            item.IsFavourite = !target;
        }
    }

    [RelayCommand]
    private void OpenAlbum(AlbumItem item) => _navigation.NavigateTo(PageKeys.Album, item.Album.Id);

    [RelayCommand]
    private void OpenArtist(ArtistItem item) => _navigation.NavigateTo(PageKeys.Artist, item.Artist.Id);

    /// <summary>Loads the period; a load started later wins.</summary>
    private async Task ShowAsync()
    {
        var generation = ++_generation;
        _load?.Cancel();
        _load?.Dispose();
        _load = new CancellationTokenSource();
        var cancellationToken = _load.Token;
        var period = Period;
        IsLoading = true;
        LoadFailed = false;
        try
        {
            var report = await _statistics.GetReportAsync(period, cancellationToken);
            var songs = await DetailsAsync<Track>(report.Totals.TopSongs, ids => _library.GetTracksByIdAsync(ids, cancellationToken), t => t.Id);
            var albums = await DetailsAsync<Album>(
                report.Totals.TopAlbums,
                async ids => (await _library.GetAlbumsAsync(new AlbumQuery { Ids = ids, Limit = ids.Count }, cancellationToken)).Items,
                a => a.Id);
            var artists = await DetailsAsync<Artist>(report.Totals.TopArtists, ids => _library.GetArtistsByIdAsync(ids, cancellationToken), a => a.Id);
            if (generation == _generation)
            {
                Apply(report, songs, albums, artists);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            if (generation == _generation)
            {
                Apply(null, [], [], []);
                LoadFailed = true;
                _errors.Report(ex, Operations.LoadStatistics);
            }
        }
        catch (OperationCanceledException)
        {
            // Another period was chosen, or the page was left.
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// The songs, albums or artists of a top list, in its order, with their plays. Ones the server no longer has are
    /// left out; without the server (offline) the list is empty, and the numbers still show.
    /// </summary>
    private static async Task<List<(T Item, int Plays)>> DetailsAsync<T>(
        IReadOnlyList<RankedId> ranked,
        Func<IReadOnlyList<string>, Task<IReadOnlyList<T>>> load,
        Func<T, string> id)
    {
        if (ranked.Count == 0)
        {
            return [];
        }

        IReadOnlyList<T> items;
        try
        {
            items = await load([.. ranked.Select(r => r.Id)]);
        }
        catch (JellyfinException)
        {
            return [];
        }

        var byId = items.DistinctBy(id, StringComparer.Ordinal).ToDictionary(id, StringComparer.Ordinal);
        return [.. ranked.Where(r => byId.ContainsKey(r.Id)).Select(r => (byId[r.Id], r.Plays))];
    }

    private void Apply(ListeningReport? report, List<(Track Item, int Plays)> songs, List<(Album Item, int Plays)> albums, List<(Artist Item, int Plays)> artists)
    {
        _report = report;
        PreviousPeriodCommand.NotifyCanExecuteChanged();
        NextPeriodCommand.NotifyCanExecuteChanged();
        var totals = report?.Totals ?? ListeningTotals.Empty;
        var previous = report?.Previous;
        var culture = CultureInfo.CurrentCulture;
        var previousName = Period.Previous.Name(culture);

        HasPlays = totals.Plays > 0;
        IsEmpty = report is not null && totals.Plays == 0;
        ListeningTime = Durations.Total(totals.Time, _strings);
        Plays = totals.Plays.ToString("N0", culture);
        Songs = totals.SongCount.ToString("N0", culture);
        Artists = totals.ArtistCount.ToString("N0", culture);
        ListeningTimeChange = Change(totals.Time.TotalSeconds, previous?.Time.TotalSeconds, previousName);
        PlaysChange = Change(totals.Plays, previous?.Plays, previousName);
        SongsChange = Change(totals.SongCount, previous?.SongCount, previousName);
        ArtistsChange = Change(totals.ArtistCount, previous?.ArtistCount, previousName);
        Coverage = report is null ? string.Empty
            : report.Period.IsAllTime ? Format("Statistics_ChartsSince", report.HistoryStart.ToString("D", culture))
            : report.IsPartial ? Format("Statistics_Since", report.HistoryStart.ToString("D", culture))
            : string.Empty;

        var byMonth = report is { Period.Range: StatisticsRange.Year or StatisticsRange.AllTime };
        var activity = report?.Activity ?? [];
        Fill(Activity, activity.Select(b => (BarName(b.Start, byMonth, culture), b.Plays)).ToList());
        ActivityStart = activity.Count > 0 ? BarName(activity[0].Start, byMonth, culture) : string.Empty;
        ActivityEnd = activity.Count > 0 ? BarName(activity[activity.Count - 1].Start, byMonth, culture) : string.Empty;
        var busiest = activity.Where(b => b.Plays > 0).OrderByDescending(b => b.Plays).ThenBy(b => b.Start).FirstOrDefault();
        ActivitySummary = busiest is null ? string.Empty
            : Format("Statistics_MostOn", BarName(busiest.Start, byMonth, culture), PlaysText(busiest.Plays));

        var hours = report?.Hours ?? [];
        Fill(Hours, hours.Select((plays, hour) => (HourName(hour, culture), plays)).ToList());
        var peak = hours.Count == 0 ? -1 : hours.Max() > 0 ? hours.ToList().IndexOf(hours.Max()) : -1;
        HoursSummary = peak < 0 ? string.Empty : Format("Statistics_MostAt", HourName(peak, culture));

        TopArtists.Replace(artists.Select(a => new ArtistItem(a.Item, ImageRequest.Primary(a.Item.Id, a.Item.PrimaryImageTag, TileWidth, DisplayScale))
        {
            PlaysText = PlaysText(a.Plays),
        }));
        TopAlbums.Replace(albums.Select(a => new AlbumItem(a.Item, ImageRequest.Primary(a.Item.Id, a.Item.PrimaryImageTag, TileWidth, DisplayScale))
        {
            PlaysText = PlaysText(a.Plays),
        }));

        _songs =
        [
            .. songs.Select((s, i) => new OnRepeatItem(
                s.Item,
                i + 1,
                s.Plays,
                PlaysText(s.Plays),
                _favourites.IsFavourite(s.Item.Id, s.Item.IsFavorite))),
        ];
        HasSongs = _songs.Count > 0;
        HasMoreSongs = _songs.Count > SongsShown;
        SongsExpanded = false;
        ShowSongs();
    }

    private void ShowSongs()
    {
        var count = SongsExpanded ? _songs.Count : Math.Min(SongsShown, _songs.Count);
        while (TopSongs.Count > count)
        {
            TopSongs.RemoveAt(TopSongs.Count - 1);
        }

        for (var i = 0; i < count; i++)
        {
            if (i < TopSongs.Count)
            {
                if (!ReferenceEquals(TopSongs[i], _songs[i]))
                {
                    TopSongs[i] = _songs[i];
                }
            }
            else
            {
                TopSongs.Add(_songs[i]);
            }
        }
    }

    /// <summary>Fills a chart with bars as high as their plays, the most plays <see cref="ChartHeight"/>.</summary>
    private void Fill(ObservableCollection<ChartBar> chart, List<(string Name, int Plays)> bars)
    {
        var most = bars.Count == 0 ? 0 : bars.Max(b => b.Plays);
        chart.Clear();
        foreach (var (name, plays) in bars)
        {
            var height = most == 0 || plays == 0 ? 0 : Math.Max(2, Math.Round(ChartHeight * plays / most));
            chart.Add(new ChartBar(height, Format("Statistics_Bar", name, PlaysText(plays))));
        }
    }

    /// <summary>The change from the period before, e.g. "+12% compared with October 2026"; none without one or from nothing.</summary>
    private string Change(double current, double? previous, string previousName)
    {
        if (previous is not { } before || before <= 0)
        {
            return string.Empty;
        }

        var change = (current - before) / before;
        var text = change.ToString("+0%;-0%;0%", CultureInfo.CurrentCulture);
        return Format("Statistics_Change", text, previousName);
    }

    private static string BarName(DateTime start, bool byMonth, CultureInfo culture) =>
        start.ToString(byMonth ? culture.DateTimeFormat.YearMonthPattern : culture.DateTimeFormat.MonthDayPattern, culture);

    private static string HourName(int hour, CultureInfo culture) =>
        new DateTime(2000, 1, 1, hour, 0, 0, DateTimeKind.Unspecified).ToString("t", culture);

    private string PlaysText(int plays) => plays == 1
        ? _strings.GetString("Statistics_OnePlay")
        : _strings.GetString("OnRepeat_Plays").Replace("{0}", plays.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal);

    private string Format(string key, params string[] values)
    {
        var text = _strings.GetString(key);
        for (var i = 0; i < values.Length; i++)
        {
            text = text.Replace($"{{{i}}}", values[i], StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>A heart was toggled elsewhere (now playing bar, favourites page, a context menu).</summary>
    private void OnFavouriteChanged(FavouriteChangedMessage message)
    {
        foreach (var item in _songs.Where(s => s.Track.Id == message.ItemId))
        {
            item.IsFavourite = message.IsFavourite;
        }
    }
}
