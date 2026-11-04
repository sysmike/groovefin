using System.Net;
using System.Net.Http.Headers;
using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

public sealed class DownloadSourceTests : IDisposable
{
    private const string TrackId = "11111111222233334444555555555555";
    private readonly JellyfinTestHost _host = new();

    public DownloadSourceTests() => _host.ConnectAsTestUser();

    public void Dispose() => _host.Dispose();

    private IDownloadSource Source => _host.Services.GetRequiredService<IDownloadSource>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<byte[]> ReadAsync(DownloadResponse response)
    {
        using var copy = new MemoryStream();
        await response.Content.CopyToAsync(copy, Ct);
        return copy.ToArray();
    }

    [Fact]
    public async Task The_original_is_the_stored_file_with_the_token_in_the_header()
    {
        _host.Server.Given(Request.Create().WithPath($"/Audio/{TrackId}/stream").WithParam("static", "true").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "audio/flac").WithBody([1, 2, 3, 4]));

        using var response = await Source.OpenAsync(TrackId, new DownloadFormat(DownloadFormatKind.Original), 0, Ct);

        (await ReadAsync(response)).Should().Equal(1, 2, 3, 4);
        response.Extension.Should().Be(".flac");
        response.Length.Should().Be(4);
        response.Resumed.Should().BeFalse();
        var request = _host.Server.LogEntries.Single().RequestMessage!;
        request.Headers!["Authorization"].Single().Should().Contain($"Token=\"{JellyfinTestHost.Token}\"");
        request.Url.Should().NotContain(JellyfinTestHost.Token);
    }

    [Fact]
    public async Task An_offset_asks_for_the_rest_and_a_partial_answer_resumes()
    {
        _host.Server.Given(Request.Create().WithPath($"/Audio/{TrackId}/stream").UsingGet().WithHeader("Range", "bytes=3-"))
            .RespondWith(Response.Create().WithStatusCode(206).WithHeader("Content-Type", "audio/mpeg").WithBody([4, 5]));

        using var response = await Source.OpenAsync(TrackId, new DownloadFormat(DownloadFormatKind.Original), 3, Ct);

        response.Resumed.Should().BeTrue();
        response.Extension.Should().Be(".mp3");
        (await ReadAsync(response)).Should().Equal(4, 5);
    }

    [Fact]
    public async Task A_server_that_refuses_the_stream_gets_the_download_endpoint()
    {
        _host.Server.Given(Request.Create().WithPath($"/Audio/{TrackId}/stream").UsingGet()).RespondWith(Response.Create().WithStatusCode(403));
        _host.Server.Given(Request.Create().WithPath($"/Items/{TrackId}/Download").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/octet-stream")
                .WithHeader("Content-Disposition", "attachment; filename=\"01 Digital Love.FLAC\"")
                .WithBody([9]));

        using var response = await Source.OpenAsync(TrackId, new DownloadFormat(DownloadFormatKind.Original), 0, Ct);

        response.Extension.Should().Be(".flac", "the file name's extension wins over the content type");
        (await ReadAsync(response)).Should().Equal(9);
    }

    [Fact]
    public async Task Aac_comes_from_the_universal_endpoint_at_the_bitrate_without_a_redirect()
    {
        _host.Server.Given(Request.Create().WithPath($"/Audio/{TrackId}/universal").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "audio/aac").WithBody([7]));

        using var response = await Source.OpenAsync(TrackId, new DownloadFormat(DownloadFormatKind.Aac, 192), 10, Ct);

        response.Extension.Should().Be(".aac");
        var request = _host.Server.LogEntries.Single().RequestMessage!;
        request.Query!["MaxStreamingBitrate"].Single().Should().Be("192000");
        request.Query["AudioCodec"].Single().Should().Be("aac");
        request.Query["EnableRedirection"].Single().Should().Be("false");
        request.Headers!.Should().NotContainKey("Range", "a transcode always starts at the beginning");
    }

    [Fact]
    public async Task A_refused_download_is_a_JellyfinException_with_the_status()
    {
        _host.Server.Given(Request.Create().WithPath($"/Audio/{TrackId}/universal").UsingGet()).RespondWith(Response.Create().WithStatusCode(500));

        var act = () => Source.OpenAsync(TrackId, new DownloadFormat(DownloadFormatKind.Aac, 256), 0, Ct);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [InlineData("audio/x-m4a", null, ".m4a")]
    [InlineData("audio/ogg", "attachment; filename=\"a.b/../evil\"", ".ogg")]
    [InlineData("application/octet-stream", null, ".audio")]
    public void The_extension_comes_from_the_name_or_the_content_type(string type, string? disposition, string expected)
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        if (disposition is not null)
        {
            content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(disposition);
        }

        JellyfinDownloadSource.ExtensionOf(content.Headers).Should().Be(expected);
    }
}
