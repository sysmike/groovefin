using Groovefin.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

public sealed class AuthenticationServiceTests : IDisposable
{
    private readonly JellyfinTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private IAuthenticationService Auth => _host.Services.GetRequiredService<IAuthenticationService>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void StubSignIn(int status, object body) =>
        _host.Server.Given(Request.Create().WithPath("/Users/AuthenticateByName").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBodyAsJson(body));

    [Fact]
    public async Task SignIn_posts_credentials_and_maps_the_result()
    {
        StubSignIn(200, new
        {
            AccessToken = "new-token",
            ServerId = "f00d",
            User = new { Id = "0123456789abcdef0123456789abcdef", Name = "Mike" },
        });

        var user = await Auth.SignInAsync(_host.ServerUrl, " mike ", "s3cret", Ct);

        user.Should().Be(new AuthenticatedUser("f00d", "0123456789abcdef0123456789abcdef", "Mike", "new-token"));
        var request = _host.Server.LogEntries.Single().RequestMessage!;
        request.Body.Should().Contain("\"Username\":\"mike\"").And.Contain("\"Pw\":\"s3cret\"");
        request.Headers!["Authorization"].Single().Should().Contain("Client=\"Groovefin\"").And.NotContain("Token=");
    }

    [Fact]
    public async Task Wrong_credentials_are_reported_as_unauthorized_and_not_retried()
    {
        StubSignIn(401, new { });

        var act = () => Auth.SignInAsync(_host.ServerUrl, "mike", "wrong", Ct);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.IsUnauthorized.Should().BeTrue();
        _host.Server.LogEntries.Should().HaveCount(1);
    }

    [Fact]
    public async Task Incomplete_response_is_rejected()
    {
        StubSignIn(200, new { ServerId = "f00d" });

        var act = () => Auth.SignInAsync(_host.ServerUrl, "mike", "pw", Ct);

        await act.Should().ThrowAsync<JellyfinException>();
    }

    private void StubJson(string path, int status, object body, bool post = false)
    {
        var request = Request.Create().WithPath(path);
        _host.Server.Given(post ? request.UsingPost() : request.UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBodyAsJson(body));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task QuickConnect_enabled_state_is_read(bool enabled)
    {
        StubJson("/QuickConnect/Enabled", 200, enabled);

        (await Auth.IsQuickConnectEnabledAsync(_host.ServerUrl, Ct)).Should().Be(enabled);
    }

    [Fact]
    public async Task QuickConnect_start_returns_code_and_secret_without_a_token()
    {
        StubJson("/QuickConnect/Initiate", 200, new { Code = "123456", Secret = "the-secret", Authenticated = false }, post: true);

        var request = await Auth.StartQuickConnectAsync(_host.ServerUrl, Ct);

        request.Should().Be(new QuickConnectRequest("123456", "the-secret"));
        request.ToString().Should().Be("123456");
        var header = _host.Server.LogEntries.Single().RequestMessage!.Headers!["Authorization"].Single();
        header.Should().Contain("DeviceId=").And.NotContain("Token=");
    }

    [Fact]
    public async Task QuickConnect_start_on_a_disabled_server_is_unauthorized()
    {
        StubJson("/QuickConnect/Initiate", 401, new { }, post: true);

        var act = () => Auth.StartQuickConnectAsync(_host.ServerUrl, Ct);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.IsUnauthorized.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuickConnect_approval_is_polled_with_the_secret(bool approved)
    {
        StubJson("/QuickConnect/Connect", 200, new { Code = "123456", Secret = "the-secret", Authenticated = approved });

        (await Auth.IsQuickConnectApprovedAsync(_host.ServerUrl, "the-secret", Ct)).Should().Be(approved);
        _host.Server.LogEntries.Single().RequestMessage!.Query!["secret"].Single().Should().Be("the-secret");
    }

    [Fact]
    public async Task QuickConnect_unknown_secret_is_not_found()
    {
        StubJson("/QuickConnect/Connect", 404, new { });

        var act = () => Auth.IsQuickConnectApprovedAsync(_host.ServerUrl, "expired", Ct);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task QuickConnect_sign_in_posts_the_secret_and_maps_the_result()
    {
        StubJson(
            "/Users/AuthenticateWithQuickConnect",
            200,
            new { AccessToken = "qc-token", ServerId = "f00d", User = new { Id = "0123456789abcdef0123456789abcdef", Name = "Mike" } },
            post: true);

        var user = await Auth.SignInWithQuickConnectAsync(_host.ServerUrl, "the-secret", Ct);

        user.Should().Be(new AuthenticatedUser("f00d", "0123456789abcdef0123456789abcdef", "Mike", "qc-token"));
        _host.Server.LogEntries.Single().RequestMessage!.Body.Should().Contain("\"Secret\":\"the-secret\"");
    }

    [Fact]
    public async Task QuickConnect_sign_in_without_user_name_is_rejected()
    {
        StubJson(
            "/Users/AuthenticateWithQuickConnect",
            200,
            new { AccessToken = "qc-token", ServerId = "f00d", User = new { Id = "0123456789abcdef0123456789abcdef" } },
            post: true);

        var act = () => Auth.SignInWithQuickConnectAsync(_host.ServerUrl, "the-secret", Ct);

        await act.Should().ThrowAsync<JellyfinException>();
    }

    [Fact]
    public async Task SignOut_ends_the_session_with_the_token()
    {
        _host.Server.Given(Request.Create().WithPath("/Sessions/Logout").UsingPost()).RespondWith(Response.Create().WithStatusCode(204));
        _host.ConnectAsTestUser();

        await Auth.SignOutAsync(Ct);

        _host.Server.LogEntries.Single().RequestMessage!.Headers!["Authorization"].Single()
            .Should().Contain($"Token=\"{JellyfinTestHost.Token}\"");
    }

    [Fact]
    public async Task SignOut_failure_is_ignored()
    {
        _host.Server.Given(Request.Create().WithPath("/Sessions/Logout").UsingPost()).RespondWith(Response.Create().WithStatusCode(500));
        _host.ConnectAsTestUser();

        var act = () => Auth.SignOutAsync(Ct);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SignOut_without_connection_sends_nothing()
    {
        await Auth.SignOutAsync(Ct);

        _host.Server.LogEntries.Should().BeEmpty();
    }
}
