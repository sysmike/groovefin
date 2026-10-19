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
        try
        {
            return await call().ConfigureAwait(false)
                ?? throw new JellyfinException($"{operation}: the server returned an empty response.");
        }
        catch (ApiException ex)
        {
            throw new JellyfinException($"{operation} failed with HTTP {ex.ResponseStatusCode}.", (HttpStatusCode)ex.ResponseStatusCode, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new JellyfinException($"{operation}: the server could not be reached.", ex.StatusCode, ex);
        }
        catch (Exception ex) when (ex is TimeoutRejectedException || (ex is TaskCanceledException && ex.InnerException is TimeoutException))
        {
            // Attempt or total timeout of the resilience pipeline, or HttpClient.Timeout.
            throw new JellyfinException($"{operation}: the server did not respond in time.", null, ex);
        }
        catch (BrokenCircuitException ex)
        {
            // Too many recent failures: the circuit breaker fails fast instead of waiting again.
            throw new JellyfinException($"{operation}: the server is not responding.", null, ex);
        }
    }
}
