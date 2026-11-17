using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ppapd.Daemon;

/// <summary>
/// One Native Messaging message on standard input or output: its UTF-8 length in four bytes of native (little-endian) order,
/// then the JSON (docs/PRD.md, §7).
/// </summary>
internal static class NativeMessage
{
    /// <summary>The largest message Chrome sends to a host, 64 MiB.</summary>
    public const int MaxIncomingBytes = 64 * 1024 * 1024;

    /// <summary>The largest message Chrome accepts from a host, 1 MB.</summary>
    public const int MaxOutgoingBytes = 1024 * 1024;

    /// <summary>The next message on <paramref name="input"/>, or <c>null</c> when the input ends before one starts.</summary>
    /// <exception cref="EndOfStreamException">The input ends inside a message.</exception>
    /// <exception cref="InvalidDataException">The length is beyond <see cref="MaxIncomingBytes"/>, or the body is not a JSON object.</exception>
    public static JsonObject? Read(Stream input)
    {
        Span<byte> header = stackalloc byte[4];
        var got = input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (got == 0)
        {
            return null;
        }
        if (got < header.Length)
        {
            throw new EndOfStreamException();
        }
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaxIncomingBytes)
        {
            throw new InvalidDataException($"A message of {length} bytes is beyond what Chrome sends.");
        }
        var body = new byte[length];
        input.ReadExactly(body);
        try
        {
            return JsonNode.Parse(body) as JsonObject ?? throw new InvalidDataException("A message must be a JSON object.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("A message is not JSON.", e);
        }
    }

    /// <summary>Writes <paramref name="message"/> to <paramref name="output"/> and flushes it.</summary>
    /// <exception cref="InvalidDataException">The message is beyond <see cref="MaxOutgoingBytes"/>; nothing has been written.</exception>
    public static void Write(Stream output, JsonObject message)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        if (body.Length > MaxOutgoingBytes)
        {
            throw new InvalidDataException($"A reply of {body.Length} bytes is beyond what Chrome accepts.");
        }
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        output.Write(header);
        output.Write(body);
        output.Flush();
    }
}