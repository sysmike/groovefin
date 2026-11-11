using System.Net;
using System.Text.Json;
using Groovefin.Core.About;
using Groovefin.Core.Updates;

namespace Groovefin.Core.Tests.Updates;

public sealed class GitHubReleaseSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (GitHubReleaseSource Sut, StubHandler Handler) Create(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (new GitHubReleaseSource(new HttpClient(handler), new AppVersion("0.9.0-beta1", "abc1234")), handler);
    }

    [Fact]
    public async Task The_latest_release_is_read_from_its_tag_and_page()
    {
        var (sut, handler) = Create(HttpStatusCode.OK, """
            {"tag_name":"v1.2.0","html_url":"https://github.com/sysmike/groovefin/releases/tag/v1.2.0","prerelease":false,"draft":false,"body":"notes"}
            """);

        var release = await sut.GetLatestAsync(Ct);

        release.Should().Be(new ReleaseInfo("1.2.0", new Uri("https://github.com/sysmike/groovefin/releases/tag/v1.2.0")));
        handler.Request!.RequestUri.Should().Be(ProjectLinks.LatestReleaseApi);
        handler.Request.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task The_request_tells_only_the_apps_name_and_version()
    {
        var (sut, handler) = Create(HttpStatusCode.OK, """{"tag_name":"v1.2.0"}""");

        await sut.GetLatestAsync(Ct);

        handler.Request!.Headers.Select(h => h.Key).Should().BeEquivalentTo(["User-Agent", "Accept"]);
        handler.Request.Headers.GetValues("User-Agent").Should().Equal("Groovefin/0.9.0-beta1");
        handler.Request.Headers.Authorization.Should().BeNull();
        handler.Request.Content.Should().BeNull();
    }

    [Fact]
    public async Task Without_a_final_release_there_is_none()
    {
        var (sut, _) = Create(HttpStatusCode.NotFound, """{"message":"Not Found"}""");

        (await sut.GetLatestAsync(Ct)).Should().BeNull();
    }

    [Theory]
    [InlineData("""{"tag_name":"v1.2.0-beta1","prerelease":true}""")]
    [InlineData("""{"tag_name":"v1.2.0","draft":true}""")]
    [InlineData("""{"name":"no tag"}""")]
    [InlineData("""[]""")]
    public async Task A_pre_release_a_draft_or_another_answer_is_no_release(string body)
    {
        var (sut, _) = Create(HttpStatusCode.OK, body);

        (await sut.GetLatestAsync(Ct)).Should().BeNull();
    }

    [Theory]
    [InlineData("""{"tag_name":"v1.2.0","html_url":"https://example.com/get-groovefin"}""")]
    [InlineData("""{"tag_name":"v1.2.0","html_url":"file:///C:/Windows/System32/calc.exe"}""")]
    [InlineData("""{"tag_name":"v1.2.0","html_url":"https://github.com/someone-else/groovefin/releases/tag/v1.2.0"}""")]
    [InlineData("""{"tag_name":"v1.2.0"}""")]
    public async Task A_page_that_is_not_the_projects_gives_way_to_the_release_list(string body)
    {
        var (sut, _) = Create(HttpStatusCode.OK, body);

        (await sut.GetLatestAsync(Ct))!.Page.Should().Be(ProjectLinks.Releases);
    }

    [Fact]
    public async Task A_refusal_and_a_broken_answer_are_errors()
    {
        var (limited, _) = Create(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}""");
        var (broken, _) = Create(HttpStatusCode.OK, "<html>");

        await limited.Awaiting(s => s.GetLatestAsync(Ct)).Should().ThrowAsync<HttpRequestException>();
        await broken.Awaiting(s => s.GetLatestAsync(Ct)).Should().ThrowAsync<JsonException>();
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
