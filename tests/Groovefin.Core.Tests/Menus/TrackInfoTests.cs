using Groovefin.Core.Menus;
using Groovefin.Core.Models;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Menus;

public sealed class TrackInfoTests
{
    private static readonly DateTimeOffset Added = new(2024, 5, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Played = new(2026, 10, 27, 21, 15, 0, TimeSpan.Zero);

    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public TrackInfoTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        foreach (var key in new[] { "Info_GainValue", "Info_BitrateValue", "Info_SampleRateValue", "Info_BitDepthValue", "Info_ChannelCount", "Info_ProviderId", "Size_Megabytes" })
        {
            _strings.GetString(key).Returns($"{key}({{0}})");
        }
    }

    private static TrackDetails Full => new()
    {
        Track = new Track
        {
            Id = "t1",
            Name = "Digital Love",
            AlbumName = "Discovery",
            AlbumArtist = "Daft Punk",
            Artists = [new ArtistRef("ar1", "Daft Punk"), new ArtistRef("ar2", "Romanthony")],
            Duration = TimeSpan.FromSeconds(298),
            TrackNumber = 3,
            DiscNumber = 1,
            Year = 2001,
            DateAdded = Added,
            IsFavorite = true,
            PlayCount = 7,
            LastPlayed = Played,
            NormalizationGain = -6.24,
            AlbumNormalizationGain = 1.5,
        },
        AlbumArtists = ["Daft Punk"],
        Genres = ["Electronic", "House"],
        ReleaseDate = new DateOnly(2001, 3, 12),
        People =
        [
            new TrackPerson("Thomas Bangalter", PersonRole.Composer),
            new TrackPerson("Romanthony", PersonRole.Lyricist),
            new TrackPerson("Guy-Manuel de Homem-Christo", PersonRole.Composer),
        ],
        Studios = ["Virgin"],
        Tags = ["Summer"],
        ProviderIds =
        [
            KeyValuePair.Create("AudioDbAlbum", "2109614"),
            KeyValuePair.Create("MusicBrainzTrack", "f2a1b9e3-0000-4000-8000-000000000001"),
        ],
        HasLyrics = false,
        Container = "flac",
        Codec = "flac",
        Bitrate = 1_031_499,
        SampleRate = 44_100,
        BitDepth = 16,
        Channels = 2,
        Size = 38_502_400,
        Path = @"\\nas\music\Daft Punk\Discovery\03 Digital Love.flac",
    };

    private static string[] Rows(DetailSection section) => [.. section.Rows.Select(r => $"{r.Key}={r.Value}")];

    [Fact]
    public void Every_known_field_is_shown_under_its_heading()
    {
        using var culture = new CultureScope("en-US");

        var sections = TrackInfo.Sections(Full, _strings);

        sections.Select(s => s.Heading).Should().Equal("Info_SectionTags", "Info_SectionFile", "Info_SectionLibrary");
        Rows(sections[0]).Should().Equal(
            "Info_Title=Digital Love",
            "Info_Artists=Daft Punk, Romanthony",
            "Info_Album=Discovery",
            "Info_AlbumArtists=Daft Punk",
            "Info_Track=3",
            "Info_Disc=1",
            "Info_ReleaseDate=3/12/2001",
            "Info_Genres=Electronic, House",
            "InfoRole_Composer=Thomas Bangalter, Guy-Manuel de Homem-Christo",
            "InfoRole_Lyricist=Romanthony",
            "Info_Label=Virgin",
            "Info_Length=4:58",
            "Info_TrackGain=Info_GainValue(-6.2)",
            "Info_AlbumGain=Info_GainValue(+1.5)",
            "Info_Lyrics=Info_No",
            "Info_ServerTags=Summer",
            "Info_ProviderId(AudioDbAlbum)=2109614",
            "Info_MusicBrainzTrack=f2a1b9e3-0000-4000-8000-000000000001");
        Rows(sections[1]).Should().Equal(
            "Info_Format=FLAC",
            "Info_Codec=FLAC",
            "Info_Bitrate=Info_BitrateValue(1,031)",
            "Info_SampleRate=Info_SampleRateValue(44.1)",
            "Info_BitDepth=Info_BitDepthValue(16)",
            "Info_Channels=Info_Stereo",
            "Info_Size=Size_Megabytes(37)",
            @"Info_Path=\\nas\music\Daft Punk\Discovery\03 Digital Love.flac");
        Rows(sections[2]).Should().Equal(
            $"Info_DateAdded={Added.ToLocalTime():g}",
            $"Info_LastPlayed={Played.ToLocalTime():g}",
            "Info_PlayCount=7",
            "Info_Favourite=Info_Yes");
    }

    [Fact]
    public void Numbers_and_dates_use_the_current_culture()
    {
        using var culture = new CultureScope("de-DE");

        var rows = TrackInfo.Sections(Full, _strings).SelectMany(Rows).ToList();

        rows.Should().Contain("Info_ReleaseDate=12.03.2001")
            .And.Contain("Info_TrackGain=Info_GainValue(-6,2)")
            .And.Contain("Info_Bitrate=Info_BitrateValue(1.031)")
            .And.Contain("Info_SampleRate=Info_SampleRateValue(44,1)");
    }

    [Fact]
    public void Unknown_fields_and_empty_headings_are_left_out()
    {
        var sections = TrackInfo.Sections(new TrackDetails { Track = new Track { Id = "t1", Name = "Untitled" } }, _strings);

        sections.Select(s => s.Heading).Should().Equal("Info_SectionTags", "Info_SectionLibrary");
        Rows(sections[0]).Should().Equal("Info_Title=Untitled");
        Rows(sections[1]).Should().Equal("Info_PlayCount=0", "Info_Favourite=Info_No");
    }

    [Fact]
    public void Only_the_year_is_shown_without_a_release_date_and_the_album_artist_comes_from_the_track()
    {
        var details = Full with { ReleaseDate = null, AlbumArtists = [] };

        var rows = Rows(TrackInfo.Sections(details, _strings)[0]);

        rows.Should().Contain("Info_Year=2001").And.NotContain(r => r.StartsWith("Info_ReleaseDate", StringComparison.Ordinal));
        rows.Should().Contain("Info_AlbumArtists=Daft Punk");
    }

    [Theory]
    [InlineData(1, "Info_Mono")]
    [InlineData(6, "Info_ChannelCount(6)")]
    public void Channels_are_named(int channels, string expected)
    {
        var rows = Rows(TrackInfo.Sections(Full with { Channels = channels }, _strings)[1]);

        rows.Should().Contain($"Info_Channels={expected}");
    }

    [Fact]
    public void A_list_of_containers_gets_spaces()
    {
        var rows = Rows(TrackInfo.Sections(Full with { Container = "mov,mp4,m4a" }, _strings)[1]);

        rows.Should().Contain("Info_Format=MOV, MP4, M4A");
    }
}
