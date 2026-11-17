using System.Text.Json;
using System.Text.Json.Nodes;
using Ppapd.ZipExtraction;

namespace Ppapd.Daemon;

/// <summary>Answers one message of docs/PRD.md §7 at a time.</summary>
/// <param name="settings">Reads <c>rules.yaml</c> afresh for each message, so an edit takes effect without a restart.</param>
/// <param name="ledger">The delivery ledger (F-6.4, F-6.5).</param>
/// <param name="work">Where ZIPs are extracted before delivery (F-4.8).</param>
/// <param name="reveal">Opens a delivery folder in Explorer (F-2.3).</param>
/// <param name="log">
/// Records an error. Lines hold no body, password, subject or plain address (S-4, S-6), only rules, paths and exception types.
/// </param>
internal sealed class MessageHandler(
    Func<Settings> settings, DeliveryLedger ledger, WorkFolders work, Action<DirectoryInfo> reveal, Action<string> log)
{
    private static readonly JsonNamingPolicy StateNames = JsonNamingPolicy.CamelCase;

    /// <summary>
    /// The reply to <paramref name="request"/>; every failure becomes <c>ok: false</c> with an <c>error</c>, so one bad
    /// message never stops the daemon. An unexpected exception is logged by its type alone, since its message may quote the input.
    /// </summary>
    public JsonObject Handle(JsonObject request)
    {
        var reply = new JsonObject { ["seq"] = request["seq"]?.DeepClone() };
        try
        {
            foreach (var (name, value) in Answer(request))
            {
                reply[name] = value;
            }
            reply["ok"] = true;
        }
        catch (RefusedException e)
        {
            Fail(reply, e.Message);
        }
        catch (InvalidRulesException e)
        {
            log($"rules.yaml: {e.Message}");
            Fail(reply, "rules.yaml cannot be used.");
        }
        catch (Exception e)
        {
            log($"{request["type"]}: {e.GetType().Name}");
            Fail(reply, "The request failed.");
        }
        return reply;
    }

    private static void Fail(JsonObject reply, string error)
    {
        reply["ok"] = false;
        reply["error"] = error;
    }

    private IEnumerable<(string, JsonNode?)> Answer(JsonObject request) => (string?)request["type"] switch
    {
        "match" => [("matches", Match(MailOf(request)))],
        "download" => [("state", Name(Download(MailOf(request), Text(request, "path"))))],
        "status" => [("states", States(request))],
        "reveal" => Reveal(Text(request, "messageId")),
        "process" => throw new RefusedException("Pairing a ZIP with its password is not implemented yet."),
        _ => throw new RefusedException("Unknown message type."),
    };

    /// <summary>Delivers the downloaded file at <paramref name="path"/> for <paramref name="mail"/> (F-5, F-6).</summary>
    private DeliveryState Download(Mail mail, string path)
    {
        var current = settings();
        FileInfo file;
        try
        {
            file = DownloadPath.Accept(path, current.DownloadFolder);
        }
        catch (OutsideDownloadFolderException)
        {
            throw new RefusedException("The path is not a file under the download folder.");
        }
        var sha256 = Convert.ToHexStringLower(NumberedCopy.Sha256Of(file));
        if (ledger.Has(mail.MessageId, sha256))
        {
            return ledger.StateOf(mail.MessageId);
        }
        var isZip = file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
        if (isZip && IsEncrypted(file))
        {
            throw new RefusedException("Pairing a ZIP with its password is not implemented yet.");
        }
        var folder = FolderFor(current.Rules, mail);
        var created = CreateMissing(folder);
        try
        {
            ledger.Record(isZip ? DeliverZip(mail, file, sha256, folder) : DeliverFile(mail, file, sha256, folder));
        }
        catch
        {
            RemoveIfEmpty(created);
            throw;
        }
        return ledger.StateOf(mail.MessageId);
    }

    private bool Match(Mail mail)
    {
        var decision = DeliveryFolder.Matches(settings().Rules, mail.Metadata);
        LogTimedOut(decision.TimedOut);
        return decision.Matches;
    }

    private void LogTimedOut(IEnumerable<ProjectRule> rules)
    {
        foreach (var rule in rules)
        {
            log($"subject_regex ran past {DeliveryFolder.MatchTimeout.TotalMilliseconds} ms and counted as not matching: {rule.SubjectRegex}");
        }
    }

    /// <summary>Creates <paramref name="folder"/> and its missing parents (F-6.1).</summary>
    /// <returns>The folders created, outermost first.</returns>
    private static List<DirectoryInfo> CreateMissing(DirectoryInfo folder)
    {
        var missing = new List<DirectoryInfo>();
        for (var current = folder; current is { Exists: false }; current = current.Parent)
        {
            missing.Insert(0, current);
        }
        folder.Create();
        return missing;
    }

    /// <summary>Removes the folders a failed delivery created, innermost first, leaving any that something else filled meanwhile (F-4.8).</summary>
    private static void RemoveIfEmpty(List<DirectoryInfo> created)
    {
        foreach (var folder in Enumerable.Reverse(created))
        {
            try
            {
                folder.Delete();
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    private DirectoryInfo FolderFor(DeliveryRules rules, Mail mail)
    {
        try
        {
            var decision = DeliveryFolder.For(rules, mail.Metadata);
            LogTimedOut(decision.TimedOut);
            return decision.Folder;
        }
        catch (OutsideProjectsRootException e)
        {
            log(e.Message);
            throw new RefusedException("The delivery folder is not under projects_root.");
        }
    }

    private static bool IsEncrypted(FileInfo zip)
    {
        using var stream = zip.OpenRead();
        return ZipExtractor.IsEncrypted(stream);
    }

    private Delivery DeliverZip(Mail mail, FileInfo zip, string sha256, DirectoryInfo folder)
    {
        var result = ZipDelivery.Deliver(zip, folder, password: null, work);
        return new Delivery(mail.MessageId, sha256, folder.FullName, [.. result.Written, .. result.Found], result.Blocked is not null);
    }

    private static Delivery DeliverFile(Mail mail, FileInfo file, string sha256, DirectoryInfo folder)
    {
        var outcome = NumberedCopy.To(file, new FileInfo(Path.Combine(folder.FullName, file.Name)), existing => NumberedCopy.SameContent(file, existing));
        return new Delivery(mail.MessageId, sha256, folder.FullName, [outcome.File.Name]);
    }

    private JsonObject States(JsonObject request)
    {
        var states = new JsonObject();
        foreach (var id in request["messageIds"] as JsonArray ?? throw new RefusedException("messageIds is missing."))
        {
            var messageId = (string?)id ?? throw new RefusedException("messageIds must hold strings.");
            states[messageId] = Name(ledger.StateOf(messageId));
        }
        return states;
    }

    /// <summary>Opens the delivery folder the ledger recorded for the mail, never a path from the message (S-5).</summary>
    private IEnumerable<(string, JsonNode?)> Reveal(string messageId)
    {
        var delivery = ledger.DeliveriesOf(messageId).LastOrDefault() ?? throw new RefusedException("Nothing was delivered for this mail.");
        var folder = new DirectoryInfo(delivery.Folder);
        if (!folder.Exists)
        {
            throw new RefusedException("The delivery folder no longer exists.");
        }
        reveal(folder);
        return [];
    }

    private static string Name(DeliveryState state) => StateNames.ConvertName(state.ToString());

    private static string Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw new RefusedException($"{name} is missing.");

    private static Mail MailOf(JsonObject request)
    {
        var mail = request["mail"] as JsonObject ?? throw new RefusedException("mail is missing.");
        return new Mail(
            Text(mail, "messageId"),
            new MailMetadata(Text(mail, "from"), Text(mail, "subject"), DateTimeOffset.Parse(Text(mail, "received"), System.Globalization.CultureInfo.InvariantCulture)));
    }

    private sealed record Mail(string MessageId, MailMetadata Metadata);

    /// <summary>A request answered with <c>ok: false</c> and this message as its <c>error</c>.</summary>
    private sealed class RefusedException(string message) : Exception(message);
}
