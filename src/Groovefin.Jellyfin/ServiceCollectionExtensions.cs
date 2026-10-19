using Groovefin.Core.Api;
using Groovefin.Core.Images;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Groovefin.Jellyfin;

/// <summary>DI registration for the Jellyfin layer.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IJellyfinClient"/> and the focused services. The caller registers a
    /// <see cref="ClientIdentity"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureResilience">Optional changes to the HTTP resilience defaults (tests use it to remove retry delays).</param>
    public static IServiceCollection AddGroovefinJellyfin(
        this IServiceCollection services,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        services.AddHttpClient(JellyfinApiFactory.HttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                ConfigureDefaults(options);
                configureResilience?.Invoke(options);
            });

        services.AddSingleton<JellyfinApiFactory>();
        services.AddSingleton<JellyfinClient>();
        services.AddSingleton<IJellyfinClient>(sp => sp.GetRequiredService<JellyfinClient>());
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IImageSource, JellyfinImageSource>();
        services.AddSingleton<IStreamUrlProvider, StreamUrlBuilder>();
        services.AddSingleton<IPlaybackReporter, PlaybackReporter>();
        return services;
    }

    /// <summary>
    /// Retry transient failures (network errors, timeouts, 408, 429, 5xx) of safe requests up to three
    /// times with exponential back-off. 4xx such as 401 are never retried. Each attempt may take 15 s,
    /// the whole request 45 s.
    /// </summary>
    internal static void ConfigureDefaults(HttpStandardResilienceOptions options)
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(500);
        options.Retry.UseJitter = true;

        // Retrying POST/DELETE could add a track to a playlist twice.
        options.Retry.DisableForUnsafeHttpMethods();

        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(45);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    }
}
