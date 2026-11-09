namespace Groovefin.Core.Playback;

/// <summary>
/// Tells a stream that failed because of the network from one that can't be played (#385). A network failure is
/// worth trying again where it stopped; an unplayable file, a refused request (4xx) or a decoding error is not.
/// </summary>
public static class StreamFailures
{
    /// <summary>The <c>HTTP_E_STATUS_*</c> results: <c>0x80190000</c> plus the HTTP status.</summary>
    private const int HttpStatusBase = unchecked((int)0x80190000);

    /// <summary>WinINet's errors (12001–12175) as results: <c>0x80072EE1</c>–<c>0x80072F8F</c>.</summary>
    private const int WinInetFirst = unchecked((int)0x80072EE1);
    private const int WinInetLast = unchecked((int)0x80072F8F);

    /// <summary>Win32 network errors as results: the network name went away, refused, unreachable, timed out.</summary>
    private static readonly HashSet<int> Win32Network =
    [
        unchecked((int)0x80070040), // ERROR_NETNAME_DELETED
        unchecked((int)0x80070079), // ERROR_SEM_TIMEOUT
        unchecked((int)0x800704C9), // ERROR_CONNECTION_REFUSED
        unchecked((int)0x800704CA), // ERROR_GRACEFUL_DISCONNECT
        unchecked((int)0x800704CF), // ERROR_NETWORK_UNREACHABLE
        unchecked((int)0x800704D0), // ERROR_HOST_UNREACHABLE
        unchecked((int)0x800704D4), // ERROR_CONNECTION_ABORTED
        unchecked((int)0x800705B4), // ERROR_TIMEOUT
    ];

    /// <summary>
    /// Whether the failure came from the network. <paramref name="error"/> is the platform's error kind (for Windows,
    /// <c>MediaPlayerError</c> or <c>MediaPlaybackItemErrorCode</c>, e.g. <c>NetworkError</c>); <paramref name="result"/>
    /// the HRESULT behind it, when known.
    /// </summary>
    public static bool IsNetwork(string? error, int? result)
    {
        if (result is { } hr && HttpStatus(hr) is { } status)
        {
            // The server answered: a timeout, too many requests or a server error may pass, a refusal does not.
            return status is 408 or 429 or >= 500;
        }

        if (result is { } code && ((code >= WinInetFirst && code <= WinInetLast) || Win32Network.Contains(code)))
        {
            return true;
        }

        return string.Equals(error, "NetworkError", StringComparison.Ordinal);
    }

    /// <summary>The HTTP status of an <c>HTTP_E_STATUS_*</c> result, or <see langword="null"/>.</summary>
    private static int? HttpStatus(int result)
    {
        var status = result - HttpStatusBase;
        return status is >= 100 and < 600 ? status : null;
    }
}
