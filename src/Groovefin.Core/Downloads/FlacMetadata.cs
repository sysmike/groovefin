namespace Groovefin.Core.Downloads;

/// <summary>
/// Moves the audio of a downloaded FLAC file to the front when it starts far in (#365). Windows' FLAC reader cannot
/// open a file whose audio starts 8 MB in, behind a large cover, but plays it without the picture and padding
/// blocks. A file whose audio starts more than <see cref="MaxAudioOffset"/> in is rewritten without them; the
/// stream info, the seek table (its offsets count from the first frame), the tags and the audio stay.
/// </summary>
internal static class FlacMetadata
{
    /// <summary>The furthest the audio may start: 2.7 MB in played, 8.1 MB did not.</summary>
    public const long MaxAudioOffset = 1 << 20;

    private const int StreamInfoType = 0;
    private const int PaddingType = 1;
    private const int PictureType = 6;
    private const int LastBlockFlag = 0x80;
    private const string TempExtension = ".flac-tmp";

    /// <summary>Rewrites the file at <paramref name="path"/> when it is a FLAC file whose audio starts too far in.</summary>
    /// <returns>The file's new size, or <see langword="null"/> when it stays as it is.</returns>
    /// <exception cref="IOException">The file is in use or cannot be read or written.</exception>
    public static long? Compact(string path)
    {
        var temp = path + TempExtension;
        try
        {
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                if (Blocks(source) is not { } blocks || source.Position <= MaxAudioOffset)
                {
                    return null;
                }

                var audioStart = source.Position;
                var kept = blocks.Where(b => b.Type is not (PaddingType or PictureType)).ToList();
                if (kept.Count == 0 || kept[0].Type != StreamInfoType)
                {
                    return null;
                }

                using var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                target.Write("fLaC"u8);
                var header = new byte[4];
                for (var i = 0; i < kept.Count; i++)
                {
                    var block = kept[i];
                    header[0] = (byte)(block.Type | (i == kept.Count - 1 ? LastBlockFlag : 0));
                    header[1] = (byte)(block.Length >> 16);
                    header[2] = (byte)(block.Length >> 8);
                    header[3] = (byte)block.Length;
                    target.Write(header);
                    source.Position = block.Start;
                    Copy(source, target, block.Length);
                }

                source.Position = audioStart;
                source.CopyTo(target);
            }

            File.Move(temp, path, overwrite: true);
            return new FileInfo(path).Length;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>
    /// The metadata blocks, with the stream after the last one, where the audio starts; <see langword="null"/> for
    /// another file or a broken one.
    /// </summary>
    private static List<(int Type, long Start, int Length)>? Blocks(FileStream file)
    {
        Span<byte> header = stackalloc byte[4];
        if (file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length || !header.SequenceEqual("fLaC"u8))
        {
            return null;
        }

        var blocks = new List<(int Type, long Start, int Length)>();
        while (true)
        {
            if (file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
            {
                return null;
            }

            var start = file.Position;
            var length = (header[1] << 16) | (header[2] << 8) | header[3];
            if (start + length > file.Length)
            {
                return null;
            }

            blocks.Add((header[0] & ~LastBlockFlag, start, length));
            file.Position = start + length;
            if ((header[0] & LastBlockFlag) != 0)
            {
                return blocks;
            }
        }
    }

    private static void Copy(Stream source, Stream target, int length)
    {
        var buffer = new byte[Math.Min(length, 81920)];
        for (var left = length; left > 0;)
        {
            var read = source.Read(buffer, 0, Math.Min(left, buffer.Length));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            target.Write(buffer, 0, read);
            left -= read;
        }
    }
}
