using Groovefin.Core.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Groovefin.Jellyfin;

/// <summary>DI registration for the Jellyfin layer.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IJellyfinClient"/> and the focused services. The caller registers a
    /// <see cref="ClientIdentity"/>.
    /// </summary>
    public static IServiceCollection AddGroovefinJellyfin(this IServiceCollection services)
    {
        // TODO(#13): add the standard resilience handler (retry, timeouts, circuit breaker).
        services.AddHttpClient(JellyfinApiFactory.HttpClientName);

        services.AddSingleton<JellyfinApiFactory>();
        services.AddSingleton<JellyfinClient>();
        services.AddSingleton<IJellyfinClient>(sp => sp.GetRequiredService<JellyfinClient>());
        services.AddSingleton<ILibraryService, LibraryService>();
        return services;
    }
}
