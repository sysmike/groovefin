using System.Net;
using Groovefin.Core.Api;
using Groovefin.Core.Images;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

public sealed class ImageSourceTests : IDisposable
{
    private const string ItemId = "11111111222233334444555555555555";
    private readonly JellyfinTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private IImageSource Source => _host.Services.GetRequiredService<IImageSource>();

    [Theory]
    [InlineData("http://media:8096", "http://media:8096/Items/abc/Images/Primary?tag=t1&maxWidth=256&quality=90")]
    [InlineData("https://host/jellyfin", "https://host/jellyfin/Items/abc/Images/Primary?tag=t1&maxWidth=256&quality=90")]
    [InlineData("https://host/jellyfin/", "https://host/jellyfin/Items/abc/Images/Primary?tag=t1&maxWidth=256&quality=90")]
    public void Url_contains_size_quality_and_tag_and_keeps_the_base_path(string server, string expected)
    {
        var url = ImageUrl.Build(new Uri(server), new ImageRequest("abc", ImageKind.Primary, "t1", 200));

        url.AbsoluteUri.Should().Be(expected);
    }

    [Fact]
    public void ServerKey_is_null_until_connected()
    {
        Source.ServerKey.Should().BeNull();

        _host.ConnectAsTestUser();

        Source.ServerKey.Should().Be(_host.ServerUrl.AbsoluteUri.TrimEnd('/').ToLowerInvariant());
    }

    [Fact]
    public async Task OpenAsync_downloads_the_sized_image_with_authorization()
    {
        _host.Server.Given(Request.Create().WithPath($"/Items/{ItemId}/Images/Primary").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "image/jpeg").WithBody([1, 2, 3]));
        _host.ConnectAsTestUser();

        await using var stream = await Source.OpenAsync(new ImageRequest(ItemId, ImageKind.Primary, "t1", 500), TestContext.Current.CancellationToken);

        stream.Length.Should().Be(3);
        var request = _host.Server.LogEntries.Single().RequestMessage!;
        request.Query!["maxWidth"].Single().Should().Be("512");
        request.Query["quality"].Single().Should().Be("90");
        request.Headers!["Authorization"].Single().Should().Contain($"Token=\"{JellyfinTestHost.Token}\"");
    }

    [Fact]
    public async Task OpenAsync_missing_image_becomes_JellyfinException()
    {
        _host.Server.Given(Request.Create().WithPath($"/Items/{ItemId}/Images/Primary").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        _host.ConnectAsTestUser();

        var act = () => Source.OpenAsync(new ImageRequest(ItemId, ImageKind.Primary, "t1", 256), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OpenAsync_without_connection_is_rejected()
    {
        var act = () => Source.OpenAsync(new ImageRequest(ItemId, ImageKind.Primary, "t1", 256), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<JellyfinException>();
    }
}
