namespace Groovefin.Core.Downloads;

/// <summary>
/// Turns the pictures of a downloaded FLAC file into padding, in place (#361). Windows' FLAC reader cannot open
/// some files with a large cover and tags: one with an 8 MB cover and 6 KB of tags did not play, but did with
/// either block turned into padding. The app shows the server's covers, never a file's own. The tags and the
/// audio stay, and the file keeps its size.
/// </summary>
internal static class FlacCovers
{
    private const int PictureType = 6;
    private const int PaddingType = 1;
    private const int LastBlockFlag = 0x80;

    /// <summary>Turns the pictures of the file at <paramref name="path"/> into padding when it is a FLAC file.</summary>
    /// <returns>The number of pictures turned into padding: 0 for another file or one without pictures.</returns>
    /// <exception cref="IOException">The file is in use or cannot be read.</exception>
    public static int Clear(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Span<byte> header = stackalloc byte[4];
        if (file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length || !header.SequenceEqual("fLaC"u8))
        {
            return 0;
        }

        var cleared = 0;
        byte[]? zeros = null;
        while (file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length)
        {
            var start = file.Position;
            var length = (header[1] << 16) | (header[2] << 8) | header[3];
            if (start + length > file.Length)
            {
                // A broken file stays as it is from here on.
                break;
            }

            if ((header[0] & ~LastBlockFlag) == PictureType)
            {
                file.Position = start - header.Length;
                file.WriteByte((byte)((header[0] & LastBlockFlag) | PaddingType));
                file.Position = start;
                zeros ??= new byte[81920];
                for (var left = length; left > 0; left -= zeros.Length)
                {
                    file.Write(zeros, 0, Math.Min(left, zeros.Length));
                }

                cleared++;
            }

            if ((header[0] & LastBlockFlag) != 0)
            {
                break;
            }

            file.Position = start + length;
        }

        return cleared;
    }
}
