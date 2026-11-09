using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Playback;

public sealed class StreamQualityTests
{
    private static readonly StreamOptions Original = new(null, TranscodingCodec.Aac);
    private static readonly StreamOptions Remote = new(320, TranscodingCodec.Aac);

    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public StreamQualityTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("StreamQuality_ConvertedShort").Returns("converted");
        _strings.GetString("StreamQuality_Downloaded").Returns("downloaded");
    }

    private static TrackDetails File(string container, string codec, int? bitrate = null, int? rate = null, int? depth = null) => new()
    {
        Track = new Track { Id = "t", Name = "t" },
        Container = container,
        Codec = codec,
        Bitrate = bitrate,
        SampleRate = rate,
        BitDepth = depth,
    };

    [Fact]
    public void A_lossless_file_without_a_limit_plays_as_it_is()
    {
        var info = StreamQuality.Describe(File("flac", "flac", 900_000, 44100, 16), Original, downloaded: false, _strings);

        info.Should().Be(new StreamQualityInfo(StreamKind.Original, "FLAC 44.1 kHz/16-bit", "StreamQuality_OriginalWhy"));
    }

    [Fact]
    public void A_file_above_the_limit_is_converted_to_the_codec_at_the_limit()
    {
        var info = StreamQuality.Describe(File("flac", "flac", 900_000, 96000, 24), Remote, downloaded: false, _strings);

        info.Kind.Should().Be(StreamKind.Converted);
        info.Label.Should().Be("AAC 320 kbps · converted");
        info.Explanation.Should().Be("StreamQuality_BitrateWhy");
    }

    [Fact]
    public void A_lossy_file_within_the_limit_shows_its_bitrate()
    {
        StreamQuality.Describe(File("mp3", "mp3", 256_000), Remote, downloaded: false, _strings).Label.Should().Be("MP3 256 kbps");
    }

    [Fact]
    public void A_format_windows_cannot_play_is_converted()
    {
        var info = StreamQuality.Describe(File("ogg", "opus", 160_000), Original, downloaded: false, _strings);

        info.Label.Should().Be("AAC · converted");
        info.Explanation.Should().Be("StreamQuality_FormatWhy");
    }

    [Theory]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "aac", true)]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "alac", true)]
    [InlineData("mp4", "ac3", false)]
    [InlineData("wav", "pcm_s16le", true)]
    [InlineData("ogg", "vorbis", false)]
    public void The_servers_direct_play_list_decides(string container, string codec, bool playable)
    {
        StreamQuality.IsDirectPlayable(File(container, codec)).Should().Be(playable);
    }

    [Fact]
    public void A_downloaded_file_says_so()
    {
        StreamQuality.Describe(File("flac", "flac", 900_000, 44100, 16), Remote, downloaded: true, _strings).Label
            .Should().Be("FLAC 44.1 kHz/16-bit · downloaded", "a download is not limited by the streaming settings");
    }

    [Fact]
    public async Task The_monitor_names_the_playing_song_and_follows_the_settings()
    {
        var engine = new FakePlaybackEngine();
        var library = Substitute.For<ILibraryService>();
        var client = Substitute.For<IJellyfinClient>();
        var settings = new InMemorySettingsService();
        client.Connection.Returns(new ServerConnection(new Uri("https://music.example/"), "u", "t"));
        library.GetTrackDetailsAsync("t", Arg.Any<CancellationToken>()).Returns(File("flac", "flac", 900_000, 44100, 16));
        using var sut = new StreamQualityMonitor(engine, library, client, settings, _strings);

        engine.Load(new PlaybackSource("q1", new Uri("https://music.example/stream"), new Track { Id = "t", Name = "t" }));
        await sut.LastUpdate;
        sut.Label.Should().Be("AAC 320 kbps · converted", "a remote server streams at 320 kbps by default");

        settings.Update(s => s with { Streaming = s.Streaming with { RemoteMaxBitrateKbps = null } });
        sut.Label.Should().Be("FLAC 44.1 kHz/16-bit");

        engine.Stop();
        await sut.LastUpdate;
        sut.HasLabel.Should().BeFalse();
    }
}
