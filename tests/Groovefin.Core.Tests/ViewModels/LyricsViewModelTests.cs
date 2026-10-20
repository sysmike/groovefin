using Groovefin.Core.Api;
using Groovefin.Core.Lyrics;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ReturnsExtensions;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class LyricsViewModelTests : IDisposable
{
    private static readonly Track Song = new() { Id = "t1", Name = "Digital Love" };
    private static readonly Track Other = new() { Id = "t2", Name = "Harder" };
    private static readonly TrackLyrics Synced = LrcParser.Parse("[00:10.00]One\n[00:20.00]\n[00:30.00]Three");

    private readonly FakeTimeProvider _time = new();
    private readonly FakePlaybackEngine _engine;
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly ILyricsService _lyrics = Substitute.For<ILyricsService>();
    private readonly InMemorySettingsService _settings = new();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly LyricsViewModel _sut;

    public LyricsViewModelTests()
    {
        _engine = new FakePlaybackEngine(time: _time);
        _player.Engine.Returns(_engine);
        _player.Queue.Returns(new QueueManager());
        _lyrics.GetLyricsAsync("t1", Arg.Any<CancellationToken>()).Returns(Synced);
        var strings = Substitute.For<IStringResources>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _sut = new LyricsViewModel(_player, _lyrics, _settings, _errors, strings);
    }

    public void Dispose() => _sut.Dispose();

    private void Play(Track track) => _engine.Load(new PlaybackSource("q-" + track.Id, new Uri("http://stream/" + track.Id), track));

    /// <summary>Reports a position; the engine passes on at most a few a second, so time moves on first.</summary>
    private void At(double seconds)
    {
        _time.Advance(TimeSpan.FromSeconds(1));
        _engine.SimulatePosition(TimeSpan.FromSeconds(seconds), TimeSpan.FromMinutes(4));
    }

    [Fact]
    public async Task Lyrics_load_only_while_the_pane_is_open()
    {
        Play(Song);
        await _lyrics.DidNotReceiveWithAnyArgs().GetLyricsAsync(default!, TestContext.Current.CancellationToken);

        _sut.ToggleCommand.Execute(null);
        await _sut.LoadAsync();

        _sut.IsOpen.Should().BeTrue();
        _sut.Lines.Select(l => l.Text).Should().Equal("One", "♪", "Three");
        _sut.IsSynced.Should().BeTrue();
        _sut.HasStatus.Should().BeFalse();
        await _lyrics.Received(1).GetLyricsAsync("t1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_current_line_follows_playback()
    {
        Play(Song);
        _sut.IsOpen = true;
        await _sut.LoadAsync();

        At(5);
        _sut.CurrentIndex.Should().Be(-1);
        At(12);
        _sut.CurrentIndex.Should().Be(0);
        _sut.Lines[0].IsCurrent.Should().BeTrue();
        At(31);

        _sut.CurrentIndex.Should().Be(2);
        _sut.Lines.Select(l => l.IsCurrent).Should().Equal(false, false, true);
        _sut.Lines.Select(l => l.IsDimmed).Should().Equal(true, true, false);
    }

    [Fact]
    public async Task A_line_click_seeks_to_it_with_the_offset()
    {
        Play(Song);
        _sut.IsOpen = true;
        await _sut.LoadAsync();

        _sut.OffsetLaterCommand.Execute(null);
        _sut.SeekToCommand.Execute(_sut.Lines[2]);

        _engine.Position.Should().Be(TimeSpan.FromSeconds(30.25));
    }

    [Fact]
    public async Task The_offset_moves_the_current_line_and_resets_for_the_next_track()
    {
        Play(Song);
        _sut.IsOpen = true;
        await _sut.LoadAsync();
        At(9.9);
        _sut.CurrentIndex.Should().Be(-1);

        _sut.OffsetEarlierCommand.Execute(null);

        _sut.Offset.Should().Be(-LyricsViewModel.OffsetStep);
        _sut.OffsetText.Should().Be("Lyrics_OffsetValue".Replace("{0}", (-0.25).ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal));
        _sut.CurrentIndex.Should().Be(0);
        _sut.ResetOffsetCommand.CanExecute(null).Should().BeTrue();

        Play(Other);
        await _sut.LoadAsync();
        _sut.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Unsynced_lyrics_are_text_without_a_current_line_or_offset()
    {
        _lyrics.GetLyricsAsync("t1", Arg.Any<CancellationToken>()).Returns(LrcParser.Parse("Verse\n\nChorus"));
        Play(Song);
        _sut.IsOpen = true;
        await _sut.LoadAsync();
        At(30);

        _sut.IsSynced.Should().BeFalse();
        _sut.Lines.Select(l => l.Text).Should().Equal("Verse", string.Empty, "Chorus");
        _sut.CurrentIndex.Should().Be(-1);
        _sut.Lines.Should().OnlyContain(l => !l.IsDimmed);
        _sut.OffsetLaterCommand.CanExecute(null).Should().BeFalse();
        _sut.SeekToCommand.Execute(_sut.Lines[0]);
        _engine.Position.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Without_lyrics_or_a_track_a_status_is_shown()
    {
        _sut.IsOpen = true;
        await _sut.LoadAsync();
        _sut.Status.Should().Be("Lyrics_NothingPlaying");

        _lyrics.GetLyricsAsync("t2", Arg.Any<CancellationToken>()).ReturnsNull();
        Play(Other);
        await _sut.LoadAsync();

        _sut.Status.Should().Be("Lyrics_None");
        _sut.Lines.Should().BeEmpty();
    }

    [Fact]
    public void A_failure_is_reported_and_reopening_retries()
    {
        var failure = new JellyfinException("down");
        _lyrics.GetLyricsAsync("t1", Arg.Any<CancellationToken>()).Returns<TrackLyrics?>(_ => throw failure);
        Play(Song);

        // Opening the pane loads (the substitute answers at once).
        _sut.IsOpen = true;

        _sut.Status.Should().Be("Lyrics_Failed");
        _errors.Received(1).Report(failure, Operations.LoadLyrics);

        _lyrics.GetLyricsAsync("t1", Arg.Any<CancellationToken>()).Returns(Synced);
        _sut.IsOpen = false;
        _sut.IsOpen = true;
        _sut.Lines.Should().HaveCount(3);
    }

    [Fact]
    public async Task Reopening_for_the_same_track_does_not_load_again()
    {
        Play(Song);
        _sut.IsOpen = true;
        await _sut.LoadAsync();
        _sut.IsOpen = false;
        _sut.IsOpen = true;
        await _sut.LoadAsync();

        await _lyrics.Received(1).GetLyricsAsync("t1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Font_size_and_alignment_are_remembered_and_bounded()
    {
        _sut.IncreaseFontSizeCommand.Execute(null);
        _sut.FontSize.Should().Be(22);
        for (var i = 0; i < 30; i++)
        {
            _sut.DecreaseFontSizeCommand.Execute(null);
        }

        _sut.FontSize.Should().Be(LyricsViewModel.MinFontSize);
        _sut.SetAlignmentCommand.Execute(LyricsAlignment.Left);

        _settings.Current.Lyrics.Should().Be(new LyricsSettings { FontSize = LyricsViewModel.MinFontSize, Alignment = LyricsAlignment.Left });
        _sut.Alignment.Should().Be(LyricsAlignment.Left);
    }
}
