using System.Net;
using System.Text.Json;
using Groovefin.Core.About;

namespace Groovefin.Core.Updates;

/// <summary>
/// <see cref="IReleaseSource"/> that asks GitHub for the project's latest release (#432), which GitHub defines as
/// the newest one that is neither a pre-release nor a draft. The request carries no token and nothing about the
/// user: only the app's name and version, as GitHub requires a <c>User-Agent</c>.
/// </summary>
public sealed class GitHubReleaseSource : IReleaseSource, IDisposable
{
    /// <summary>GitHub answers within a second or two; a check that hangs gives up.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly AppVersion _version;

    /// <summary>Creates the source with an HTTP client of its own, which it disposes.</summary>
    public GitHubReleaseSource(AppVersion version)
        : this(new HttpClient { Timeout = Timeout }, version)
    {
        _ownsHttp = true;
    }

    /// <summary>Creates the source on <paramref name="http"/>, which stays the caller's.</summary>
    public GitHubReleaseSource(HttpClient http, AppVersion version)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(version);
        _http = http;
        _version = version;
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProjectLinks.LatestReleaseApi);
        request.Headers.TryAddWithoutValidation("User-Agent", $"Groovefin/{_version.Version}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // No final release yet.
            return null;
        }

        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var release = document.RootElement;
            if (release.ValueKind != JsonValueKind.Object
                || !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
                || IsTrue(release, "prerelease") || IsTrue(release, "draft"))
            {
                return null;
            }

            // The link is opened in the browser: only the project's own pages, whatever the answer says.
            var page = release.TryGetProperty("html_url", out var url) && url.ValueKind == JsonValueKind.String
                && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed)
                && parsed.AbsoluteUri.StartsWith(ProjectLinks.Releases.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
                    ? parsed
                    : ProjectLinks.Releases;
            return new ReleaseInfo(tag.GetString()!.TrimStart('v', 'V'), page);
        }
    }

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
