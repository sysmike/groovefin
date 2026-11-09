using System.Globalization;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Services;

namespace Groovefin.Core.Playback;

/// <summary>What the user hears (#397): the downloaded file, the server's original file, or a conversion.</summary>
public enum StreamKind
{
    Downloaded,
    Original,
    Converted,
}

/// <summary>A song's stream quality as the now playing bar shows it, e.g. "FLAC 44.1 kHz/16-bit", with why.</summary>
public sealed record StreamQualityInfo(StreamKind Kind, string Label, string Explanation);

/// <summary>
/// Works out what plays (#397), the way the server decides it for the universal endpoint: the original file when
/// Windows plays its format and it fits the bitrate limit of the streaming settings, otherwise a conversion to the
/// chosen codec at that limit.
/// </summary>
public static class StreamQuality
{
    /// <summary>The formats Windows plays as they are: a file's container part, and its codec where that matters.</summary>
    private static readonly (string Container, string? Codec)[] DirectPlay =
    [
        ("mp3", null), ("aac", null), ("m4a", "aac"), ("m4b", "aac"), ("m4a", "alac"), ("mp4", "aac"), ("mp4", "alac"),
        ("flac", null), ("wav", null), ("wma", null), ("asf", null),
    ];

    /// <summary>
    /// Describes <paramref name="file"/>. <paramref name="options"/> are the streaming settings for the server; a
    /// <paramref name="downloaded"/> song plays from its file.
    /// </summary>
    public static StreamQualityInfo Describe(TrackDetails file, StreamOptions options, bool downloaded, IStringResources strings)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(strings);

        var playable = IsDirectPlayable(file);
        if (downloaded)
        {
            // The downloads convert what Windows cannot play (#362).
            return playable
                ? new(StreamKind.Downloaded, Join(Format(file), strings.GetString("StreamQuality_Downloaded")), strings.GetString("StreamQuality_DownloadedWhy"))
                : new(StreamKind.Downloaded, Join(strings.GetString("StreamQuality_ConvertedShort"), strings.GetString("StreamQuality_Downloaded")), strings.GetString("StreamQuality_DownloadedWhy"));
        }

        var codec = options.Codec == TranscodingCodec.Mp3 ? "MP3" : "AAC";
        if (!playable)
        {
            var target = options.MaxBitrateKbps is { } max ? $"{codec} {Kbps(max)}" : codec;
            return new(
                StreamKind.Converted,
                Join(target, strings.GetString("StreamQuality_ConvertedShort")),
                strings.GetString("StreamQuality_FormatWhy").Replace("{0}", Name(file), StringComparison.Ordinal));
        }

        if (options.MaxBitrateKbps is { } limit && file.Bitrate is { } bitrate && bitrate > limit * 1000L)
        {
            return new(
                StreamKind.Converted,
                Join($"{codec} {Kbps(limit)}", strings.GetString("StreamQuality_ConvertedShort")),
                strings.GetString("StreamQuality_BitrateWhy").Replace("{0}", Kbps(limit), StringComparison.Ordinal));
        }

        return new(StreamKind.Original, Format(file), strings.GetString("StreamQuality_OriginalWhy"));
    }

    /// <summary>Whether Windows plays the file's format without the server converting it.</summary>
    internal static bool IsDirectPlayable(TrackDetails file)
    {
        var codec = file.Codec?.ToLowerInvariant();
        var containers = (file.Container ?? string.Empty).ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return containers.Any(container => DirectPlay.Any(d => d.Container == container && (d.Codec is null || d.Codec == codec)));
    }

    /// <summary>"FLAC 44.1 kHz/16-bit" for a lossless file with a bit depth, otherwise "MP3 320 kbps".</summary>
    private static string Format(TrackDetails file)
    {
        var name = Name(file);
        if (file.BitDepth is { } depth && file.SampleRate is { } rate)
        {
            return $"{name} {(rate / 1000.0).ToString("0.#", CultureInfo.CurrentCulture)} kHz/{depth.ToString(CultureInfo.CurrentCulture)}-bit";
        }

        return file.Bitrate is { } bitrate ? $"{name} {Kbps((int)Math.Round(bitrate / 1000.0))}" : name;
    }

    /// <summary>The codec's usual name, e.g. "FLAC", "WAV" for PCM, "WMA" for the Windows Media codecs.</summary>
    private static string Name(TrackDetails file)
    {
        var codec = (file.Codec ?? file.Container?.Split(',')[0] ?? string.Empty).ToLowerInvariant();
        return codec switch
        {
            _ when codec.StartsWith("pcm", StringComparison.Ordinal) => "WAV",
            _ when codec.StartsWith("wma", StringComparison.Ordinal) => "WMA",
            "vorbis" => "Vorbis",
            "opus" => "Opus",
            "" => "?",
            _ => codec.ToUpperInvariant(),
        };
    }

    private static string Kbps(int kbps) => $"{kbps.ToString(CultureInfo.CurrentCulture)} kbps";

    private static string Join(string first, string second) => $"{first} · {second}";
}
