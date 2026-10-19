using Groovefin.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Server;

namespace Groovefin.Jellyfin.Tests;

/// <summary>A WireMock "Jellyfin server" plus the real DI registration of the Jellyfin layer.</summary>
internal sealed class JellyfinTestHost : IDisposable
{
    public static readonly ClientIdentity Identity = new("Groovefin", "1.0.0", "Test PC", "device-1");
    public const string UserId = "0123456789abcdef0123456789abcdef";
    public const string Token = "test-token";

    private readonly ServiceProvider _services;

    public JellyfinTestHost(TimeSpan? attemptTimeout = null)
    {
        Server = WireMockServer.Start();
        _services = new ServiceCollection()
            .AddLogging()
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
