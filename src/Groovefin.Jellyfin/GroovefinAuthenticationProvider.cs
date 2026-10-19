using Groovefin.Core.Api;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Groovefin.Jellyfin;

/// <summary>Adds the Groovefin authorization header to every SDK request.</summary>
internal sealed class GroovefinAuthenticationProvider(ClientIdentity identity, string? accessToken) : IAuthenticationProvider
{
    public Task AuthenticateRequestAsync(
        RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        request.Headers.Remove(AuthorizationHeader.Name);
        request.Headers.Add(AuthorizationHeader.Name, AuthorizationHeader.Build(identity, accessToken));
        return Task.CompletedTask;
    }
}
