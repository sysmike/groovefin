using System.Net;

namespace Groovefin.Core.Api;

/// <summary>
/// A request to the server failed. Thrown by all Core API services so callers never see SDK or
/// transport exception types.
/// </summary>
public class JellyfinException : Exception
{
    public JellyfinException()
    {
    }

    public JellyfinException(string message)
        : base(message)
    {
    }

    public JellyfinException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public JellyfinException(string message, HttpStatusCode? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>Gets the HTTP status code, or <see langword="null"/> when the server could not be reached.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Gets a value indicating whether the server rejected the credentials (HTTP 401).</summary>
    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
}
