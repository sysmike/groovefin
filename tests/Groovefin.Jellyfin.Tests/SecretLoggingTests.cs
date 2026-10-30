using System.Collections.Concurrent;
using Groovefin.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

/// <summary>
/// Passwords, access tokens and Quick Connect secrets never reach the log (#293), at any level and from any
/// category, the HTTP client's own included: the app log is meant to be shared in bug reports.
/// </summary>
public sealed class SecretLoggingTests : IDisposable
{
    private const string Password = "pa55-w0rd-not-for-logs";
    private const string NewToken = "token-not-for-logs";
    private const string QuickConnectSecret = "qc-secret-not-for-logs";

    private readonly CapturingLoggerProvider _logs = new();
    private readonly JellyfinTestHost _host;

    public SecretLoggingTests()
    {
        _host = new JellyfinTestHost(logs: _logs);
    }

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IAuthenticationService Auth => _host.Services.GetRequiredService<IAuthenticationService>();

    private void Stub(string path, int status, object body) =>
        _host.Server.Given(Request.Create().WithPath(path))
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBodyAsJson(body));

    [Fact]
    public async Task Signing_in_and_using_the_token_logs_none_of_them()
    {
        Stub("/Users/AuthenticateByName", 200, new
        {
            AccessToken = NewToken,
            ServerId = "f00d",
            User = new { Id = JellyfinTestHost.UserId, Name = "Mike" },
        });
        Stub("/Items", 200, new { Items = Array.Empty<object>(), TotalRecordCount = 0, StartIndex = 0 });

        var user = await Auth.SignInAsync(_host.ServerUrl, "mike", Password, Ct);
        _host.Client.Connect(new ServerConnection(_host.ServerUrl, user.UserId, user.AccessToken));
        await _host.Library.GetAlbumsAsync(new AlbumQuery(), Ct);

        _logs.Entries.Should().NotBeEmpty("the HTTP client logs its requests at these levels");
        _logs.Entries.Should().NotContain(e => e.Contains(Password, StringComparison.Ordinal))
            .And.NotContain(e => e.Contains(NewToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_sign_in_logs_no_password()
    {
        Stub("/Users/AuthenticateByName", 401, new { });

        var act = () => Auth.SignInAsync(_host.ServerUrl, "mike", Password, Ct);

        await act.Should().ThrowAsync<JellyfinException>();
        _logs.Entries.Should().NotContain(e => e.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Quick_Connect_logs_no_secret()
    {
        Stub("/QuickConnect/Initiate", 200, new { Code = "123456", Secret = QuickConnectSecret });
        Stub("/QuickConnect/Connect", 200, new { Authenticated = true, Code = "123456", Secret = QuickConnectSecret });

        var request = await Auth.StartQuickConnectAsync(_host.ServerUrl, Ct);
        await Auth.IsQuickConnectApprovedAsync(_host.ServerUrl, request.Secret, Ct);

        _logs.Entries.Should().NotContain(e => e.Contains(QuickConnectSecret, StringComparison.Ordinal));
    }

    /// <summary>Keeps every message with its exception and scope values, as one string per entry.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyCollection<string> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                entries.Enqueue($"{category} scope: {state}");
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
                entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} [{values}] {exception}");
            }
        }
    }
}
