using Groovefin.Core.Lyrics;

namespace Groovefin.Core.Tests.Lyrics;

public sealed class LrcParserTests
{
    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void Timed_lines_become_synced_lyrics_in_time_order()
    {
        var lyrics = LrcParser.Parse("[00:12.50]Second\n[00:01.00]First\n[01:02.345]Third");

        lyrics.IsSynced.Should().BeTrue();
        lyrics.Lines.Should().Equal(
            new LyricLine(Ms(1_000), "First"),
            new LyricLine(Ms(12_500), "Second"),
            new LyricLine(Ms(62_345), "Third"));
    }

    [Theory]
    [InlineData("[00:07]x", 7_000)]
    [InlineData("[00:07.5]x", 7_500)]
    [InlineData("[00:07.25]x", 7_250)]
    [InlineData("[00:07.125]x", 7_125)]
    [InlineData("[00:07:25]x", 7_250)]
    [InlineData("[61:00.00]x", 3_660_000)]
    [InlineData("[01:01:00.00]x", 3_660_000)]
    public void Time_tags_are_read_in_every_common_form(string line, int expectedMs) =>
        LrcParser.Parse(line).Lines.Should().ContainSingle().Which.Start.Should().Be(Ms(expectedMs));

    [Fact]
    public void A_line_with_several_time_tags_is_repeated_at_each_time()
    {
        var lyrics = LrcParser.Parse("[00:10.00][00:30.00]Chorus\n[00:20.00]Verse");

        lyrics.Lines.Should().Equal(
            new LyricLine(Ms(10_000), "Chorus"),
            new LyricLine(Ms(20_000), "Verse"),
            new LyricLine(Ms(30_000), "Chorus"));
    }

    [Fact]
    public void Lines_at_the_same_time_keep_their_file_order()
    {
        var lyrics = LrcParser.Parse("[00:05.00]Lead\n[00:05.00]Backing");

        lyrics.Lines.Select(l => l.Text).Should().Equal("Lead", "Backing");
    }

    [Fact]
    public void Metadata_tags_are_read_and_not_shown_as_lines()
    {
        var lyrics = LrcParser.Parse("[ti:Digital Love]\n[ar:Daft Punk]\n[al:Discovery]\n[by:someone]\n[length: 04:58]\n[re:Editor]\n[00:01.00]Hello");

        lyrics.Metadata.Should().Be(new LyricsMetadata
        {
            Title = "Digital Love",
            Artist = "Daft Punk",
            Album = "Discovery",
            By = "someone",
            Length = TimeSpan.FromSeconds(298),
        });
        lyrics.Lines.Should().ContainSingle().Which.Text.Should().Be("Hello");
    }

    [Theory]
    [InlineData("+500", 9_500)]
    [InlineData("-250", 10_250)]
    [InlineData("0", 10_000)]
    public void The_offset_tag_moves_every_line(string offset, int expectedMs)
    {
        var lyrics = LrcParser.Parse($"[offset:{offset}]\n[00:10.00]Line");

        lyrics.Lines.Single().Start.Should().Be(Ms(expectedMs));
        lyrics.Metadata.Offset.Should().Be(Ms(int.Parse(offset, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void An_offset_never_moves_a_line_before_the_start()
    {
        LrcParser.Parse("[offset:2000]\n[00:01.00]Early").Lines.Single().Start.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Enhanced_word_times_are_dropped_and_empty_timed_lines_kept()
    {
        var lyrics = LrcParser.Parse("[00:01.00]<00:01.00>One <00:01.50>two\n[00:03.00]\n[00:05.00]Three");

        lyrics.Lines.Select(l => l.Text).Should().Equal("One two", string.Empty, "Three");
    }

    [Fact]
    public void Text_without_time_tags_is_unsynced_with_paragraph_breaks()
    {
        var lyrics = LrcParser.Parse("\r\n[ar:Daft Punk]\r\nVerse one\r\nline two\r\n\r\nVerse two\r\n\r\n");

        lyrics.IsSynced.Should().BeFalse();
        lyrics.Lines.Should().Equal(
            new LyricLine(null, "Verse one"),
            new LyricLine(null, "line two"),
            new LyricLine(null, string.Empty),
            new LyricLine(null, "Verse two"));
        lyrics.Metadata.Artist.Should().Be("Daft Punk");
    }

    [Fact]
    public void Untimed_lines_in_synced_lyrics_are_left_out()
    {
        var lyrics = LrcParser.Parse("Some header\n[00:01.00]Sung");

        lyrics.Lines.Should().Equal(new LyricLine(Ms(1_000), "Sung"));
    }

    [Theory]
    [InlineData("[00:75.00]Bad seconds")]
    [InlineData("[00:01.1234]Too precise")]
    public void Invalid_time_tags_are_ignored(string line) =>
        LrcParser.Parse(line).IsSynced.Should().BeFalse();

    [Fact]
    public void A_byte_order_mark_and_brackets_in_text_are_handled()
    {
        var lyrics = LrcParser.Parse("﻿[00:01.00]Text [with brackets]");

        lyrics.Lines.Single().Text.Should().Be("Text [with brackets]");
    }

    [Fact]
    public void Empty_input_gives_no_lines()
    {
        var lyrics = LrcParser.Parse(string.Empty);

        lyrics.Lines.Should().BeEmpty();
        lyrics.IsSynced.Should().BeFalse();
    }

    [Theory]
    [InlineData("[00:01.00]x", true)]
    [InlineData("Plain\n[01:02]x", true)]
    [InlineData("Plain text [chorus]", false)]
    [InlineData(null, false)]
    public void HasTimeTags_finds_time_tags(string? text, bool expected) =>
        LrcParser.HasTimeTags(text).Should().Be(expected);

    [Fact]
    public void LineAt_finds_the_line_being_sung()
    {
        var lyrics = LrcParser.Parse("[00:10.00]One\n[00:20.00]Two\n[00:30.00]Three");

        lyrics.LineAt(Ms(5_000)).Should().Be(-1);
        lyrics.LineAt(Ms(10_000)).Should().Be(0);
        lyrics.LineAt(Ms(25_000)).Should().Be(1);
        lyrics.LineAt(Ms(99_000)).Should().Be(2);
        lyrics.LineAt(Ms(21_000), offset: Ms(2_000)).Should().Be(0);
        lyrics.LineAt(Ms(19_000), offset: Ms(-2_000)).Should().Be(1);
    }

    [Fact]
    public void LineAt_is_minus_one_for_unsynced_lyrics() =>
        LrcParser.Parse("Plain").LineAt(TimeSpan.FromMinutes(1)).Should().Be(-1);
}
