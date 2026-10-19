using System.Net;
using Groovefin.Core.Api;
using Microsoft.Kiota.Abstractions;

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
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new JellyfinException($"{operation}: the server did not respond in time.", null, ex);
        }
    }
}
