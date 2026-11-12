// Native Messaging host for the PoC: answers each message from the extension and
// logs its own lifetime, so the log shows when Chrome starts and stops the process.
// The log records counts and timings only, never mail IDs.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ppapd-poc", "native-host.log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

var pid = Environment.ProcessId;
var started = Stopwatch.StartNew();

void Log(string text) =>
    File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} pid={pid} t={started.Elapsed.TotalSeconds:F1}s {text}{Environment.NewLine}");

Log($"start args=[{string.Join(' ', args)}]");

using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();
var received = 0;

try
{
    while (ReadMessage(stdin) is { } message)
    {
        received++;
        var convids = message["convids"]?.AsArray() ?? [];
        var fields = string.Join(' ', message
            .Where(p => p.Key is not ("convids" or "kind"))
            .Select(p => $"{p.Key}={p.Value?.ToJsonString()}"));
        Log($"message #{received} kind={message["kind"]} convids={convids.Count} {fields}");

        var reply = new JsonObject
        {
            ["seq"] = message["seq"]?.DeepClone(),
            ["pid"] = pid,
            ["received"] = received,
            ["delivered"] = new JsonArray(convids.Select(c => c?.DeepClone()).ToArray()),
        };
        WriteMessage(stdout, reply);
    }
    Log($"stdin closed after {received} messages");
}
catch (Exception e)
{
    Log($"error {e.GetType().Name}: {e.Message}");
}

static JsonObject? ReadMessage(Stream input)
{
    Span<byte> header = stackalloc byte[4];
    if (input.ReadAtLeast(header, 4, throwOnEndOfStream: false) < 4)
    {
        return null;
    }
    var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
    input.ReadExactly(body);
    return JsonNode.Parse(body)?.AsObject();
}

static void WriteMessage(Stream output, JsonObject message)
{
    var body = JsonSerializer.SerializeToUtf8Bytes(message);
    Span<byte> header = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
    output.Write(header);
    output.Write(body);
    output.Flush();
}
