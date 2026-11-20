using Groovefin.Core.Api;

namespace Groovefin.Core.Playback;

/// <summary>
/// The songs whose original file Windows could not open as a stream this session, so the server converts them. Some
/// FLAC files have a large cover together with their tags that Windows' FLAC reader rejects (#361); a download has
/// its cover removed (#364), a stream of the original does not. Since the server tells when the PC is at home (#453),
/// such files stream as they are there, without the limit's conversion that used to hide the problem.
/// </summary>
public sealed class StreamConversions
{
    /// <summary>The bitrate of a conversion when the streaming settings set no limit.</summary>
    public const int Kbps = 320;

    private readonly HashSet<string> _tracks = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Raised on the thread that added a song.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets a value indicating whether <paramref name="trackId"/> streams converted.</summary>
    public bool Has(string trackId)
    {
        lock (_gate)
        {
            return _tracks.Contains(trackId);
        }
    }

    /// <summary>The song streams converted from now on; <see langword="false"/> when it already did.</summary>
    public bool Add(string trackId)
    {
        bool added;
        lock (_gate)
        {
            added = _tracks.Add(trackId);
        }

        if (added)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return added;
    }

    /// <summary>The streaming settings' <paramref name="options"/> for <paramref name="trackId"/>: converted when it has to be.</summary>
    public StreamOptions For(string trackId, StreamOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Has(trackId) ? options with { Convert = true, MaxBitrateKbps = options.MaxBitrateKbps ?? Kbps } : options;
    }
}
