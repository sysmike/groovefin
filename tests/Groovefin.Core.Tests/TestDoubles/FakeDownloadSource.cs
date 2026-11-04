using System.Collections.Concurrent;
using System.Text;
using Groovefin.Core.Downloads;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Tests.TestDoubles;

/// <summary>Serves songs from memory and records every request; a song can fail or arrive cut short.</summary>
internal sealed class FakeDownloadSource : IDownloadSource
{
    public ConcurrentQueue<(string TrackId, string Format, long Offset)> Requests { get; } = new();

    /// <summary>Gets the files by track ID; a song without one gets four bytes of its ID.</summary>
    public Dictionary<string, byte[]> Files { get; } = [];

    /// <summary>Gets the exceptions a song's download throws.</summary>
    public Dictionary<string, Exception> Failures { get; } = [];

    /// <summary>Gets the songs that arrive with only their first bytes, although the length says more.</summary>
    public HashSet<string> CutShort { get; } = [];

    public string Extension { get; set; } = ".flac";

    public bool SupportsRange { get; set; } = true;

    public int Count(string trackId) => Requests.Count(r => r.TrackId == trackId);

    public Task<DownloadResponse> OpenAsync(string trackId, DownloadFormat format, long offset, CancellationToken cancellationToken)
    {
        Requests.Enqueue((trackId, format.Key, offset));
        if (Failures.TryGetValue(trackId, out var failure))
        {
            throw failure;
        }

        var file = FileOf(trackId);
        var resumed = offset > 0 && SupportsRange && format.Kind == DownloadFormatKind.Original;
        var bytes = resumed ? file[(int)offset..] : file;
        var sent = CutShort.Contains(trackId) ? bytes[..2] : bytes;
        var extension = format.Kind == DownloadFormatKind.Aac ? ".aac" : Extension;
        return Task.FromResult(new DownloadResponse(new MemoryStream(sent), bytes.Length, extension, resumed));
    }

    public byte[] FileOf(string trackId) =>
        Files.TryGetValue(trackId, out var file) ? file : Encoding.UTF8.GetBytes(trackId.PadRight(4, '_'));
}
