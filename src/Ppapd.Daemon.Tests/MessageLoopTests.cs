using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace Ppapd.Daemon.Tests;

public sealed class MessageLoopTests
{
    [Fact]
    public void Each_message_on_the_input_gets_its_reply_on_the_output_in_order_until_the_input_ends()
    {
        var input = Frames(new JsonObject { ["seq"] = 1 }, new JsonObject { ["seq"] = 2 });
        var output = new MemoryStream();

        MessageLoop.Run(input, output, request => new JsonObject { ["seq"] = request["seq"]!.DeepClone(), ["ok"] = true }, _ => { });

        output.Position = 0;
        Assert.Equal(1, (int)NativeMessage.Read(output)!["seq"]!);
        Assert.Equal(2, (int)NativeMessage.Read(output)!["seq"]!);
        Assert.Null(NativeMessage.Read(output));
    }

    [Fact]
    public void A_broken_frame_stops_the_loop_and_is_logged()
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, NativeMessage.MaxIncomingBytes + 1);
        var log = new List<string>();
        var answered = 0;

        MessageLoop.Run(new MemoryStream(header), new MemoryStream(), request => { answered++; return []; }, log.Add);

        Assert.Equal(0, answered);
        Assert.Single(log);
    }

    private static MemoryStream Frames(params JsonObject[] messages)
    {
        var stream = new MemoryStream();
        foreach (var message in messages)
        {
            NativeMessage.Write(stream, message);
        }
        stream.Position = 0;
        return stream;
    }
}