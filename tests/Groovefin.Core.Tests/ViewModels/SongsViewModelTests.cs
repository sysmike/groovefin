using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class SongsViewModelTests
{
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly InMemorySettingsService _settings = new();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly List<TrackQuery> _queries = [];

    public SongsViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        StubTracks(total: 450);
    }

    private static Track Song(int i) => new() { Id = $"t{i}", Name = $"Song {i}" };

    private void StubTracks(int total) =>
        _library.GetTracksAsync(Arg.Do<TrackQuery>(_queries.Add), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var q = call.Arg<TrackQuery>();
                var count = Math.Max(0, Math.Min(q.Limit, total - q.StartIndex));
                return new PagedResult<Track>([.. Enumerable.Range(q.StartIndex, count).Select(Song)], total, q.StartIndex);
            });

    private SongsViewModel CreateSut() => new(_library, _settings, _player, _errors, _strings);

    [Fact]
    public void Disposing_twice_does_not_throw()
    {
        // The page disposes it when the user leaves; the container disposes it again at shutdown (#145).
        var sut = CreateSut();
        sut.Dispose();

        var act = sut.Dispose;

        act.Should().NotThrow();
    }

    private static SongColumnViewModel Column(SongsViewModel sut, SongColumn column) => sut.Columns.Single(c => c.Column == column);

    [Fact]
    public async Task Songs_load_page_by_page_until_all_are_loaded()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();

        sut.Songs.Should().HaveCount(SongsViewModel.PageSize);
        sut.TotalCount.Should().Be(450);
        await sut.LoadMoreCommand.ExecuteAsync(null);
        await sut.LoadMoreCommand.ExecuteAsync(null);

        _queries.Select(q => q.StartIndex).Should().Equal(0, 200, 400);
        sut.Songs.Should().HaveCount(450);
        sut.LoadMoreCommand.CanExecute(null).Should().BeFalse();
        sut.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_library_shows_the_empty_state()
    {
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<Track>([], 0, 0));
        using var sut = CreateSut();

        await sut.InitializeAsync();

        sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_load_is_reported()
    {
        var failure = new JellyfinException("down");
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns<PagedResult<Track>>(_ => throw failure);
        using var sut = CreateSut();

        await sut.InitializeAsync();

        _errors.Received(1).Report(failure, Operations.LoadSongs);
        sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Clicking_a_header_sorts_by_it_and_a_second_click_reverses_the_order()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();
        var album = Column(sut, SongColumn.Album);

        sut.SortCommand.Execute(album);
        await Task.Yield();
        album.SortDescending.Should().BeFalse();
        Column(sut, SongColumn.Title).SortDescending.Should().BeNull();

        sut.SortCommand.Execute(album);
        await Task.Yield();

        album.SortDescending.Should().BeTrue();
        _queries.Last().Should().Be(new TrackQuery { StartIndex = 0, Limit = SongsViewModel.PageSize, SortBy = TrackSortField.Album, Descending = true });
        _settings.Current.Browse.SongSort.Should().Be(TrackSortField.Album);
        _settings.Current.Browse.SongSortDescending.Should().BeTrue();
    }

    [Fact]
    public async Task The_track_number_column_cannot_be_sorted()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();

        sut.SortCommand.Execute(Column(sut, SongColumn.TrackNumber));

        _queries.Should().ContainSingle();
        sut.SortBy.Should().Be(TrackSortField.Name);
    }

    [Fact]
    public void The_default_layout_shows_the_main_columns()
    {
        using var sut = CreateSut();

        sut.VisibleColumns.Select(c => c.Column).Should().Equal(
            SongColumn.TrackNumber, SongColumn.Title, SongColumn.Artist, SongColumn.Album, SongColumn.Year, SongColumn.Duration);
        sut.Columns.Should().HaveCount(Enum.GetValues<SongColumn>().Length);
        Column(sut, SongColumn.Title).Header.Should().Be("SongColumn_Title");
        Column(sut, SongColumn.Title).SortDescending.Should().BeFalse();
    }

    [Fact]
    public void Showing_hiding_resizing_and_moving_columns_is_saved()
    {
        using var sut = CreateSut();
        var layoutChanges = 0;
        sut.LayoutChanged += (_, _) => layoutChanges++;
        var year = Column(sut, SongColumn.Year);

        sut.ToggleColumnCommand.Execute(Column(sut, SongColumn.PlayCount));
        sut.ToggleColumnCommand.Execute(Column(sut, SongColumn.TrackNumber));
        sut.ResizeColumn(year, 100);
        sut.CommitLayout();
        sut.MoveColumnLeftCommand.Execute(year);

        sut.VisibleColumns.Select(c => c.Column).Should().Equal(
            SongColumn.Title, SongColumn.Artist, SongColumn.Year, SongColumn.Album, SongColumn.Duration, SongColumn.PlayCount);
        layoutChanges.Should().BeGreaterThanOrEqualTo(4);

        var saved = _settings.Current.Browse.SongColumns;
        saved.Should().HaveCount(sut.Columns.Count);
        saved.Single(c => c.Column == SongColumn.Year).Width.Should().Be(100);
        saved.Single(c => c.Column == SongColumn.TrackNumber).IsVisible.Should().BeFalse();

        using var reopened = CreateSut();
        reopened.VisibleColumns.Select(c => (c.Column, c.Width)).Should().Equal(sut.VisibleColumns.Select(c => (c.Column, c.Width)));
    }

    [Fact]
    public void The_title_cannot_be_hidden()
    {
        using var sut = CreateSut();
        var title = Column(sut, SongColumn.Title);

        sut.ToggleColumnCommand.Execute(title);

        title.IsVisible.Should().BeTrue();
        title.CanHide.Should().BeFalse();
    }

    [Fact]
    public void Widths_stay_within_bounds()
    {
        using var sut = CreateSut();
        var album = Column(sut, SongColumn.Album);

        sut.ResizeColumn(album, 5);
        album.Width.Should().Be(SongsViewModel.MinColumnWidth);
        sut.ResizeColumn(album, 5000);
        album.Width.Should().Be(SongsViewModel.MaxColumnWidth);

        sut.NarrowColumnCommand.Execute(album);
        album.Width.Should().Be(SongsViewModel.MaxColumnWidth - SongsViewModel.WidthStep);
        sut.WidenColumnCommand.Execute(album);
        album.Width.Should().Be(SongsViewModel.MaxColumnWidth);
    }

    [Fact]
    public void A_damaged_saved_layout_is_repaired()
    {
        _settings.Update(s => s with
        {
            Browse = s.Browse with
            {
                SongColumns =
                [
                    new SongColumnSettings { Column = SongColumn.Duration, Width = 90 },
                    new SongColumnSettings { Column = SongColumn.Title, Width = double.NaN, IsVisible = false },
                    new SongColumnSettings { Column = SongColumn.Duration, Width = 300 },
                    new SongColumnSettings { Column = (SongColumn)99, Width = 50 },
                ],
            },
        });

        using var sut = CreateSut();

        sut.Columns.Should().HaveCount(Enum.GetValues<SongColumn>().Length);
        // Saved columns keep their order; missing ones go after their default neighbour (the track number first).
        sut.Columns.Select(c => c.Column).Should().Equal(
            SongColumn.TrackNumber, SongColumn.Duration, SongColumn.PlayCount, SongColumn.DateAdded, SongColumn.Title,
            SongColumn.Artist, SongColumn.Album, SongColumn.AlbumArtist, SongColumn.Year);
        Column(sut, SongColumn.Duration).Width.Should().Be(90);
        Column(sut, SongColumn.Title).IsVisible.Should().BeTrue();
        Column(sut, SongColumn.Title).Width.Should().Be(280);
    }

    [Fact]
    public void Reset_restores_the_default_layout()
    {
        using var sut = CreateSut();
        sut.ToggleColumnCommand.Execute(Column(sut, SongColumn.Artist));
        sut.MoveColumn(Column(sut, SongColumn.Duration), 0);

        sut.ResetColumnsCommand.Execute(null);

        sut.VisibleColumns.Select(c => c.Column).Should().Equal(
            SongColumn.TrackNumber, SongColumn.Title, SongColumn.Artist, SongColumn.Album, SongColumn.Year, SongColumn.Duration);
        _settings.Current.Browse.SongColumns.Select(c => c.Column).Should().Equal(sut.Columns.Select(c => c.Column));
    }

    [Fact]
    public async Task Playing_a_row_plays_the_loaded_songs_from_it()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();

        sut.PlayCommand.Execute(sut.Songs[3]);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Count == SongsViewModel.PageSize && t[3].Id == "t3"), 3);
    }

    [Fact]
    public void Rows_format_every_column()
    {
        var row = new SongRow(new Track
        {
            Id = "t",
            Name = "Digital Love",
            AlbumName = "Discovery",
            AlbumArtist = "Daft Punk",
            Artists = [new ArtistRef("a1", "Daft Punk"), new ArtistRef("a2", "Romanthony")],
            Duration = TimeSpan.FromSeconds(298),
            TrackNumber = 3,
            Year = 2001,
            PlayCount = 7,
            DateAdded = new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero),
        });

        row.Text(SongColumn.TrackNumber).Should().Be("3");
        row.Text(SongColumn.Artist).Should().Be("Daft Punk, Romanthony");
        row.Text(SongColumn.AlbumArtist).Should().Be("Daft Punk");
        row.Text(SongColumn.Year).Should().Be("2001");
        row.Text(SongColumn.Duration).Should().Be("4:58");
        row.Text(SongColumn.PlayCount).Should().Be("7");
        row.Text(SongColumn.DateAdded).Should().NotBeEmpty();
        row.AccessibleName.Should().Be("Digital Love, Daft Punk, Romanthony, Discovery, 4:58");
        new SongRow(new Track { Id = "x", Name = "Bare" }).Text(SongColumn.Year).Should().BeEmpty();
    }

    [Theory]
    [InlineData("en-US", "5/1/2024")]
    [InlineData("de-DE", "01.05.2024")]
    public void Dates_follow_the_current_culture(string culture, string expected)
    {
        using var scope = new CultureScope(culture);
        var row = new SongRow(new Track { Id = "t", Name = "Digital Love", DateAdded = new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero) });

        row.Text(SongColumn.DateAdded).Should().Be(expected);
    }
}
