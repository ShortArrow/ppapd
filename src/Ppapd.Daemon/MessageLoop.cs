using System.Text.Json.Nodes;

namespace Ppapd.Daemon;

/// <summary>Reads messages from the browser and writes each reply back, one at a time (docs/PRD.md, §7).</summary>
internal static class MessageLoop
{
    /// <summary>
    /// Answers every message on <paramref name="input"/> with <paramref name="answer"/> until the input ends. A broken frame
    /// stops the loop and is logged, since where the next message starts can no longer be known.
    /// </summary>
    public static void Run(Stream input, Stream output, Func<JsonObject, JsonObject> answer, Action<string> log)
    {
        try
        {
            while (NativeMessage.Read(input) is { } request)
            {
                NativeMessage.Write(output, answer(request));
            }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            log($"Stopped reading messages: {e.Message}");
        }
    }
}