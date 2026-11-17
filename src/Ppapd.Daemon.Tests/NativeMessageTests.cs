using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace Ppapd.Daemon.Tests;

public sealed class NativeMessageTests
{
    [Fact]
    public void A_message_written_is_read_back_the_same()
    {
        var stream = new MemoryStream();
        var message = new JsonObject { ["type"] = "status", ["seq"] = 7, ["messageIds"] = new JsonArray("<a@example.co.jp>", "見積") };

        NativeMessage.Write(stream, message);
        stream.Position = 0;

        Assert.Equal(message.ToJsonString(), NativeMessage.Read(stream)!.ToJsonString());
    }

    [Fact]
    public void A_message_is_framed_as_its_utf8_length_in_four_little_endian_bytes_then_its_json()
    {
        var stream = new MemoryStream();

        NativeMessage.Write(stream, new JsonObject { ["seq"] = 1 });

        var bytes = stream.ToArray();
        var body = Encoding.UTF8.GetBytes("""{"seq":1}""");
        Assert.Equal(body.Length, BinaryPrimitives.ReadInt32LittleEndian(bytes));
        Assert.Equal(body, bytes[4..]);
    }

    [Fact]
    public void The_end_of_input_before_a_message_reads_as_no_message()
    {
        Assert.Null(NativeMessage.Read(new MemoryStream()));
    }

    [Fact]
    public void Input_ending_inside_a_message_is_refused()
    {
        var stream = new MemoryStream();
        NativeMessage.Write(stream, new JsonObject { ["seq"] = 1 });
        var truncated = new MemoryStream(stream.ToArray()[..^2]);

        Assert.Throws<EndOfStreamException>(() => NativeMessage.Read(truncated));
    }

    [Fact]
    public void A_length_beyond_what_chrome_sends_to_a_host_is_refused_before_reading_the_body()
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, NativeMessage.MaxIncomingBytes + 1);

        Assert.Throws<InvalidDataException>(() => NativeMessage.Read(new MemoryStream(header)));
    }

    [Fact]
    public void A_body_that_is_not_a_json_object_is_refused()
    {
        var body = Encoding.UTF8.GetBytes("[1,2]");
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, 4);

        Assert.Throws<InvalidDataException>(() => NativeMessage.Read(new MemoryStream(frame)));
    }

    [Fact]
    public void A_reply_beyond_what_chrome_accepts_from_a_host_is_refused_and_nothing_is_written()
    {
        var stream = new MemoryStream();
        var huge = new JsonObject { ["data"] = new string('a', NativeMessage.MaxOutgoingBytes) };

        Assert.Throws<InvalidDataException>(() => NativeMessage.Write(stream, huge));

        Assert.Equal(0, stream.Length);
    }
}
