using Groovefin.Core.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Groovefin.Jellyfin.Tests;

public sealed class StreamUrlBuilderTests : IDisposable
{
    private const string TrackId = "11112222333344445555666677778888";

    private readonly JellyfinTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private IStreamUrlProvider Sut => _host.Services.GetRequiredService<IStreamUrlProvider>();

    private static Dictionary<string, string> Query(Uri url) =>
        url.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]), StringComparer.Ordinal);

    [Fact]
    public void Url_targets_the_universal_endpoint_with_direct_play_containers_and_the_token()
    {
        _host.ConnectAsTestUser();

        var url = Sut.GetStreamUrl(TrackId, new StreamOptions(null, TranscodingCodec.Aac));

        url.GetLeftPart(UriPartial.Path).Should().Be($"{_host.ServerUrl.ToString().TrimEnd('/')}/Audio/{TrackId}/universal");
        Query(url).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["UserId"] = JellyfinTestHost.UserId,
            ["DeviceId"] = JellyfinTestHost.Identity.DeviceId,
            ["Container"] = "mp3,aac,m4a|aac,m4b|aac,m4a|alac,flac,wav,wma",
            ["TranscodingContainer"] = "aac",
            ["TranscodingProtocol"] = "http",
            ["AudioCodec"] = "aac",
            ["EnableRedirection"] = "true",
            ["EnableRemoteMedia"] = "false",
            ["ApiKey"] = JellyfinTestHost.Token,
        });
    }

    [Fact]
    public void Bitrate_limit_and_codec_are_applied()
    {
        _host.ConnectAsTestUser();

        var query = Query(Sut.GetStreamUrl(TrackId, new StreamOptions(192, TranscodingCodec.Mp3)));

        query["MaxStreamingBitrate"].Should().Be("192000");
        query["AudioCodec"].Should().Be("mp3");
        query["TranscodingContainer"].Should().Be("mp3");
    }

    [Fact]
    public void A_start_position_becomes_start_time_ticks()
    {
        _host.ConnectAsTestUser();

        var query = Query(Sut.GetStreamUrl(TrackId, new StreamOptions(320, TranscodingCodec.Aac) { StartPosition = TimeSpan.FromSeconds(90) }));

        query["StartTimeTicks"].Should().Be("900000000");
    }

    [Fact]
    public void A_base_path_is_kept_and_the_id_is_normalized()
    {
        var server = new Uri("https://media.example.com/jellyfin/");
        _host.Client.Connect(new ServerConnection(server, JellyfinTestHost.UserId, "token with space"));

        var url = Sut.GetStreamUrl("11112222-3333-4444-5555-666677778888", new StreamOptions(null, TranscodingCodec.Aac));

        url.AbsolutePath.Should().Be($"/jellyfin/Audio/{TrackId}/universal");
        Query(url)["ApiKey"].Should().Be("token with space");
    }

    [Fact]
    public void Without_a_connection_no_url_is_built()
    {
        var act = () => Sut.GetStreamUrl(TrackId, new StreamOptions(null, TranscodingCodec.Aac));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        _host.ConnectAsTestUser();
        var options = new StreamOptions(null, TranscodingCodec.Aac);

        Sut.Invoking(s => s.GetStreamUrl("not-an-id", options)).Should().Throw<ArgumentException>();
        Sut.Invoking(s => s.GetStreamUrl("", options)).Should().Throw<ArgumentException>();
        Sut.Invoking(s => s.GetStreamUrl(TrackId, null!)).Should().Throw<ArgumentNullException>();
        Sut.Invoking(s => s.GetStreamUrl(TrackId, options with { MaxBitrateKbps = 0 })).Should().Throw<ArgumentOutOfRangeException>();
        Sut.Invoking(s => s.GetStreamUrl(TrackId, options with { Codec = (TranscodingCodec)42 })).Should().Throw<ArgumentOutOfRangeException>();
    }
}
