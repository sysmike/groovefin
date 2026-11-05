namespace Groovefin.Core.Tests.TestDoubles;

/// <summary>Builds the bytes of a FLAC file: <c>fLaC</c>, metadata blocks, then the audio.</summary>
internal static class FlacFile
{
    public const int StreamInfo = 0;
    public const int Padding = 1;
    public const int SeekTable = 3;
    public const int Tags = 4;
    public const int Picture = 6;

    public static readonly byte[] Audio = [0xFF, 0xF8, 0x69, 0x08, 0x00, 0x42];

    public static byte[] Build(params (int Type, byte[] Data)[] blocks)
    {
        var bytes = new List<byte>("fLaC"u8.ToArray());
        for (var i = 0; i < blocks.Length; i++)
        {
            var (type, data) = blocks[i];
            bytes.Add((byte)(type | (i == blocks.Length - 1 ? 0x80 : 0)));
            bytes.AddRange([(byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
            bytes.AddRange(data);
        }

        bytes.AddRange(Audio);
        return [.. bytes];
    }

    /// <summary>A file with stream info, tags and a cover of <paramref name="coverSize"/> bytes.</summary>
    public static byte[] WithCover(int coverSize = 100_000) =>
        Build(
            (StreamInfo, new byte[34]),
            (Tags, "TITLE=余計なこと"u8.ToArray()),
            (Picture, [.. Enumerable.Range(0, coverSize).Select(i => (byte)(i % 251 + 1))]));

    /// <summary>The blocks of a FLAC file: type, whether it is the last one, and its data.</summary>
    public static List<(int Type, bool Last, byte[] Data)> Blocks(byte[] file)
    {
        var blocks = new List<(int, bool, byte[])>();
        var pos = 4;
        while (true)
        {
            var length = (file[pos + 1] << 16) | (file[pos + 2] << 8) | file[pos + 3];
            var last = (file[pos] & 0x80) != 0;
            blocks.Add((file[pos] & 0x7F, last, file[(pos + 4)..(pos + 4 + length)]));
            pos += 4 + length;
            if (last)
            {
                return blocks;
            }
        }
    }
}
