using System.Buffers.Binary;
using System.Text;

namespace Groovefin.Core.Discord;

/// <summary>The kinds of message on Discord's local RPC pipe.</summary>
public enum DiscordOpcode
{
    Handshake = 0,
    Frame = 1,
    Close = 2,
    Ping = 3,
    Pong = 4,
}

/// <summary>
/// One message on Discord's local RPC pipe: the opcode and the length of the body (both 32-bit little
/// endian), then the body as UTF-8 JSON.
/// </summary>
public static class DiscordFrame
{
    public const int HeaderSize = 8;

    /// <summary>Discord's own limit for one message.</summary>
    public const int MaxBodySize = 64 * 1024;

    public static byte[] Encode(DiscordOpcode opcode, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[HeaderSize + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, (int)opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, HeaderSize);
        return frame;
    }

    /// <summary>Reads one message.</summary>
    /// <exception cref="EndOfStreamException">Discord closed the pipe.</exception>
    /// <exception cref="InvalidDataException">The header announced a body larger than <see cref="MaxBodySize"/>.</exception>
    public static async Task<(DiscordOpcode Opcode, string Json)> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var opcode = (DiscordOpcode)BinaryPrimitives.ReadInt32LittleEndian(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > MaxBodySize)
        {
            throw new InvalidDataException($"Discord sent a message of {length} bytes.");
        }

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return (opcode, Encoding.UTF8.GetString(body));
    }
}
