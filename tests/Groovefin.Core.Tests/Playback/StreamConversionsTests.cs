using Groovefin.Core.Api;
using Groovefin.Core.Playback;

namespace Groovefin.Core.Tests.Playback;

public sealed class StreamConversionsTests
{
    private readonly StreamConversions _sut = new();

    [Fact]
    public void A_song_added_streams_converted_at_the_limit_or_320_kbps()
    {
        var changes = 0;
        _sut.Changed += (_, _) => changes++;

        _sut.Add("a").Should().BeTrue();
        _sut.Add("a").Should().BeFalse("it already streams converted");

        changes.Should().Be(1);
        _sut.For("a", new StreamOptions(null, TranscodingCodec.Aac)).Should().Be(new StreamOptions(320, TranscodingCodec.Aac) { Convert = true });
        _sut.For("a", new StreamOptions(192, TranscodingCodec.Mp3)).Should().Be(new StreamOptions(192, TranscodingCodec.Mp3) { Convert = true });
        _sut.For("b", new StreamOptions(null, TranscodingCodec.Aac)).Should().Be(new StreamOptions(null, TranscodingCodec.Aac));
    }

    [Theory]
    [InlineData(unchecked((int)0xC00D36C4), true)]
    [InlineData(unchecked((int)0xC00D36B4), false)]
    [InlineData(null, false)]
    public void Only_an_unsupported_byte_stream_is_a_format_windows_cannot_open(int? result, bool unreadable)
    {
        StreamFailures.IsUnreadableFormat(result).Should().Be(unreadable);
    }
}
