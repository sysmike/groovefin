using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Statistics;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class StatisticsViewModelTests
{
    private static readonly DateTime Today = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Unspecified);

    private readonly IListeningStatistics _statistics = Substitute.For<IListeningStatistics>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly List<StatisticsPeriod> _asked = [];
    private Func<StatisticsPeriod, ListeningReport> _reports;

    public StatisticsViewModelTests()
    {
        _statistics.Today.Returns(Today);
        _reports = period => Report(period, ListeningTotals.Empty);
        _statistics.GetReportAsync(Arg.Any<StatisticsPeriod>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var period = call.Arg<StatisticsPeriod>();
            _asked.Add(period);
            return _reports(period);
        });
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("OnRepeat_Plays").Returns("{0} plays");
        _strings.GetString("Statistics_OnePlay").Returns("1 play");
        _strings.GetString("Statistics_Change").Returns("{0} compared with {1}");
        _strings.GetString("Statistics_Since").Returns("Since {0}");
        _strings.GetString("Statistics_PlaylistName").Returns("Top songs: {0}");
        _strings.GetString("Statistics_MostAt").Returns("Most at {0}");
        _strings.GetString("Statistics_MostOn").Returns("Peak: {0}, {1}");
        _strings.GetString("Statistics_Bar").Returns("{0}: {1}");
        _strings.GetString("Statistics_AllTime").Returns("All time");
        _strings.GetString("Duration_HoursMinutes").Returns("{0} h {1} min");
        _strings.GetString("Duration_Minutes").Returns("{0} min");
        _library.GetTracksByIdAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyList<string>>().Where(id => id != "gone").Reverse().Select(Song).ToList());
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var ids = call.Arg<AlbumQuery>().Ids;
            return new PagedResult<Album>([.. ids.Select(id => new Album { Id = id, Name = id })], ids.Count, 0);
        });
        _library.GetArtistsByIdAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyList<string>>().Select(id => new Artist { Id = id, Name = id }).ToList());

    /// <summary>The view model in the culture the test set (US English: weeks start on Sunday).</summary>
    private StatisticsViewModel CreateSut() => new(
        _statistics,
        _library,
        _playlists,
        _player,
        _navigation,
        _errors,
        _strings,
        new FavouritesService(_library, _messenger, _errors),
        _messenger,
        new CurrentTrack(new FakePlaybackEngine()));

    private static Track Song(string id) => new() { Id = id, Name = $"Song {id}", Duration = TimeSpan.FromMinutes(3) };

    private static DateTime Day(int month, int day) => new(2026, month, day, 0, 0, 0, DateTimeKind.Unspecified);

    private static ListeningReport Report(StatisticsPeriod period, ListeningTotals totals, ListeningTotals? previous = null, DateTime? historyStart = null) =>
        new(
            period,
            totals,
            previous,
            period.IsAllTime ? [] : [new ActivityBar(period.Start, totals.Plays)],
            [.. Enumerable.Range(0, 24).Select(h => h == 21 ? totals.Plays : 0)],
            historyStart ?? Day(1, 1));

    private static ListeningTotals Totals(int plays, int songs = 0, params string[] topSongs) => new(
        plays,
        TimeSpan.FromMinutes(3 * plays),
        songs,
        2,
        [.. topSongs.Select((id, i) => new RankedId(id, topSongs.Length - i))],
        [new RankedId("album-1", plays)],
        [new RankedId("artist-1", plays), new RankedId("artist-2", 1)]);

    [Fact]
    public async Task It_opens_on_this_month_after_reading_the_plays_from_other_devices()
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        _reports = period => Report(period, Totals(12, 7, "a", "b"));

        await sut.LoadAsync();

        Received.InOrder(() =>
        {
            _statistics.SyncAsync();
            _statistics.GetReportAsync(Arg.Any<StatisticsPeriod>(), Arg.Any<CancellationToken>());
        });
        sut.Period.Should().Be(new StatisticsPeriod(StatisticsRange.Month, Day(11, 1), Day(12, 1)));
        sut.PeriodName.Should().Be("November 2026");
        sut.HasPlays.Should().BeTrue();
        sut.IsEmpty.Should().BeFalse();
        sut.Plays.Should().Be("12");
        sut.Songs.Should().Be("7");
        sut.Artists.Should().Be("2");
        sut.ListeningTime.Should().Be("36 min");
        sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task The_numbers_show_their_change_from_the_period_before_when_there_is_one()
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        _reports = period => Report(period, Totals(12, 6), previous: Totals(10, 8));

        await sut.LoadAsync();

        sut.PlaysChange.Should().Be("+20% compared with October 2026");
        sut.SongsChange.Should().Be("-25% compared with October 2026");
        sut.ArtistsChange.Should().Be("0% compared with October 2026");

        _reports = period => Report(period, Totals(12, 6));
        await sut.PreviousPeriodCommand.ExecuteAsync(null);

        sut.PlaysChange.Should().BeEmpty();
    }

    [Fact]
    public async Task A_period_the_history_began_in_says_since_when()
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        _reports = period => Report(period, Totals(3), historyStart: Day(11, 4).AddHours(9));

        await sut.LoadAsync();

        sut.Coverage.Should().Be("Since Wednesday, November 4, 2026");
        sut.HasCoverage.Should().BeTrue();
        sut.PreviousPeriodCommand.CanExecute(null).Should().BeFalse("the history has nothing before November");
    }

    [Fact]
    public async Task The_arrows_step_through_the_periods_but_not_past_today()
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        await sut.LoadAsync();

        sut.NextPeriodCommand.CanExecute(null).Should().BeFalse();
        await sut.PreviousPeriodCommand.ExecuteAsync(null);

        sut.Period.Start.Should().Be(Day(10, 1));
        sut.NextPeriodCommand.CanExecute(null).Should().BeTrue();
        _asked.Select(p => p.Start).Should().Equal(Day(11, 1), Day(10, 1));
    }

    [Theory]
    [InlineData(StatisticsRange.Week, "11/8/2026 – 11/14/2026")]
    [InlineData(StatisticsRange.Year, "2026")]
    [InlineData(StatisticsRange.AllTime, "All time")]
    public async Task A_range_shows_its_period_that_holds_today(StatisticsRange range, string name)
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.SelectRangeAsync(range);

        sut.Range.Should().Be(range);
        sut.PeriodName.Should().Be(name);
        _asked[^1].Range.Should().Be(range);
        sut.IsAllTime.Should().Be(range == StatisticsRange.AllTime);
        if (range == StatisticsRange.AllTime)
        {
            sut.PreviousPeriodCommand.CanExecute(null).Should().BeFalse();
            sut.NextPeriodCommand.CanExecute(null).Should().BeFalse();
        }
    }

    [Fact]
    public async Task An_empty_period_says_so()
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        await sut.LoadAsync();

        sut.IsEmpty.Should().BeTrue();
        sut.HasPlays.Should().BeFalse();
        sut.HasSongs.Should().BeFalse();
        sut.PlaySongsCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_load_is_reported()
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        _statistics.GetReportAsync(Arg.Any<StatisticsPeriod>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ListeningReport>(new JellyfinException("offline")));

        await sut.LoadAsync();

        sut.LoadFailed.Should().BeTrue();
        sut.IsEmpty.Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<Exception>(), Operations.LoadStatistics);
    }

    [Fact]
    public async Task The_top_songs_show_five_until_show_all_and_leave_out_songs_the_server_no_longer_has()
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        _reports = period => Report(period, Totals(30, 7, "a", "b", "gone", "c", "d", "e", "f", "g"));

        await sut.LoadAsync();

        sut.TopSongs.Select(s => s.Track.Id).Should().Equal("a", "b", "c", "d", "e");
        sut.TopSongs.Select(s => s.Number).Should().Equal("1", "2", "3", "4", "5");
        sut.TopSongs[0].PlaysCount.Should().Be("8");
        sut.HasMoreSongs.Should().BeTrue();

        sut.ToggleSongsCommand.Execute(null);

        sut.TopSongs.Select(s => s.Track.Id).Should().Equal("a", "b", "c", "d", "e", "f", "g");
        sut.SongsToggleText.Should().Be("Statistics_ShowLess");
    }

    [Fact]
    public async Task The_top_songs_play_from_a_row_and_save_as_a_playlist_named_after_the_period()
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        _reports = period => Report(period, Totals(3, 2, "a", "b"));
        _playlists.CreateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns("p1");
        await sut.LoadAsync();

        sut.PlaySongCommand.Execute(sut.TopSongs[1]);
        await sut.SaveSongsAsPlaylistCommand.ExecuteAsync(null);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Select(x => x.Id).SequenceEqual(new[] { "a", "b" })), 1);
        await _playlists.Received(1).CreateAsync(
            "Top songs: November 2026",
            Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "a", "b" })),
            Arg.Any<CancellationToken>());
        _navigation.Received(1).NavigateTo(PageKeys.Playlist, "p1");
    }

    [Fact]
    public async Task The_top_rows_show_as_many_tiles_as_fit_with_their_plays()
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        _reports = period => Report(period, Totals(5));
        await sut.LoadAsync();

        sut.TopArtists.Fit(1);

        sut.TopArtists.Items.Should().ContainSingle().Which.Should().BeOfType<ArtistItem>().Which.PlaysText.Should().Be("5 plays");
        sut.TopArtists.HasMore.Should().BeTrue();
        sut.TopArtists.ToggleCommand.Execute(null);
        sut.TopArtists.Items.Should().HaveCount(2);
        ((ArtistItem)sut.TopArtists.Items[1]).PlaysText.Should().Be("1 play");
        ((ArtistItem)sut.TopArtists.Items[1]).AccessibleName.Should().Be("artist-2, 1 play");
        sut.TopAlbums.Items.Should().ContainSingle().Which.Should().BeOfType<AlbumItem>().Which.Album.Id.Should().Be("album-1");
        sut.TopAlbums.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task The_charts_are_as_high_as_their_plays_with_their_peaks_named()
    {
        using var culture = new CultureScope("en-US");
        var sut = CreateSut();
        _reports = period => Report(period, Totals(4));

        await sut.LoadAsync();

        sut.Activity.Should().ContainSingle().Which.Should().Be(new ChartBar(StatisticsViewModel.ChartHeight, "November 1: 4 plays"));
        sut.ActivitySummary.Should().Be("Peak: November 1, 4 plays");
        sut.Hours.Should().HaveCount(24);
        sut.Hours[21].Height.Should().Be(StatisticsViewModel.ChartHeight);
        sut.Hours[20].Height.Should().Be(0);
        sut.HoursSummary.Should().MatchRegex(@"^Most at 9:00\sPM$", "ICU puts a narrow no-break space before PM");
    }

    [Fact]
    public void TilesThatFit_matches_the_rows_layout()
    {
        TileRow.TilesThatFit(100).Should().Be(1);
        TileRow.TilesThatFit(321).Should().Be(2);
        TileRow.TilesThatFit(1000).Should().Be(6);
    }
}
