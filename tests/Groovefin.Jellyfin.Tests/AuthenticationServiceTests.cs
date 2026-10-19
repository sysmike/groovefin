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
