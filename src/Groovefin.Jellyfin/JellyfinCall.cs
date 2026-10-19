using System.Net;
using Groovefin.Core.Api;
using Microsoft.Kiota.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Groovefin.Jellyfin;

/// <summary>Translates SDK and transport exceptions into <see cref="JellyfinException"/>.</summary>
internal static class JellyfinCall
{
    public static async Task<T> RunAsync<T>(Func<Task<T?>> call, string operation)
        where T : class
    {
        T? result;
        try
        {
            result = await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex, operation) is { } translated)
        {
            throw translated;
        }

        return result ?? throw new JellyfinException($"{operation}: the server returned an empty response.");
    }

    /// <summary>For responses that are a single value, such as a boolean.</summary>
    public static async Task<T> RunValueAsync<T>(Func<Task<T?>> call, string operation)
        where T : struct
    {
        T? result;
        try
        {
            result = await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex, operation) is { } translated)
        {
            throw translated;
        }

        return result ?? throw new JellyfinException($"{operation}: the server returned an empty response.");
    }

    /// <summary>For requests without a response body (POST/DELETE that return 204).</summary>
    public static async Task RunAsync(Func<Task> call, string operation)
    {
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex, operation) is { } translated)
        {
            throw translated;
        }
    }

    private static JellyfinException? Translate(Exception ex, string operation) => ex switch
    {
        ApiException api => new JellyfinException($"{operation} failed with HTTP {api.ResponseStatusCode}.", (HttpStatusCode)api.ResponseStatusCode, ex),
        HttpRequestException http => new JellyfinException($"{operation}: the server could not be reached.", http.StatusCode, ex),

        // Attempt or total timeout of the resilience pipeline, or HttpClient.Timeout.
        TimeoutRejectedException or TaskCanceledException { InnerException: TimeoutException } =>
            new JellyfinException($"{operation}: the server did not respond in time.", null, ex),

        // Too many recent failures: the circuit breaker fails fast instead of waiting again.
        BrokenCircuitException => new JellyfinException($"{operation}: the server is not responding.", null, ex),
        _ => null,
    };
}
