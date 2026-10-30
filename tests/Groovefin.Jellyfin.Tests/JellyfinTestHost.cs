using Groovefin.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireMock.Server;

namespace Groovefin.Jellyfin.Tests;

/// <summary>A WireMock "Jellyfin server" plus the real DI registration of the Jellyfin layer.</summary>
internal sealed class JellyfinTestHost : IDisposable
{
    public static readonly ClientIdentity Identity = new("Groovefin", "1.0.0", "Test PC", "device-1");
    public const string UserId = "0123456789abcdef0123456789abcdef";
    public const string Token = "test-token";

    private readonly ServiceProvider _services;

    /// <param name="attemptTimeout">A shorter timeout per attempt, for the resilience tests.</param>
    /// <param name="logs">Receives every log entry, from Trace up: the tests that no secret is logged (#293).</param>
    public JellyfinTestHost(TimeSpan? attemptTimeout = null, ILoggerProvider? logs = null)
    {
        Server = WireMockServer.Start();
        _services = new ServiceCollection()
            .AddLogging(builder =>
            {
                if (logs is not null)
                {
                    builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs);
                }
            })
            .AddSingleton(Identity)
            .AddGroovefinJellyfin(options =>
            {
                // Keep retries, but without waiting between attempts.
                options.Retry.Delay = TimeSpan.Zero;
                if (attemptTimeout is { } timeout)
                {
                    options.AttemptTimeout.Timeout = timeout;
                    options.CircuitBreaker.SamplingDuration = timeout * 2 + TimeSpan.FromSeconds(1);
                }
            })
            .BuildServiceProvider();
    }

    public WireMockServer Server { get; }

    public IServiceProvider Services => _services;

    public Uri ServerUrl => new(Server.Url!);

    public IJellyfinClient Client => _services.GetRequiredService<IJellyfinClient>();

    public ILibraryService Library => _services.GetRequiredService<ILibraryService>();

    public void ConnectAsTestUser() => Client.Connect(new ServerConnection(ServerUrl, UserId, Token));

    public void Dispose()
    {
        _services.Dispose();
        Server.Stop();
        Server.Dispose();
    }
}
