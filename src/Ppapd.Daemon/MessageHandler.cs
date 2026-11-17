using System.Text.Json;
using System.Text.Json.Nodes;
using Ppapd.ZipExtraction;

namespace Ppapd.Daemon;

/// <summary>Answers one message of docs/PRD.md §7 at a time.</summary>
/// <param name="settings">Reads <c>rules.yaml</c> afresh for each message, so an edit takes effect without a restart.</param>
/// <param name="ledger">The delivery ledger (F-6.4, F-6.5).</param>
/// <param name="store">The ZIPs and password candidates waiting for their pair (F-3).</param>
/// <param name="hashes">Turns sender addresses and domains into the HMACs pairing compares (S-6).</param>
/// <param name="work">Where ZIPs are extracted before delivery (F-4.8).</param>
/// <param name="now">The clock that ages waiting records (F-3.2, F-3.6).</param>
/// <param name="reveal">Opens a delivery folder in Explorer (F-2.3).</param>
/// <param name="log">
/// Records an error. Lines hold no body, password, subject or plain address (S-4, S-6), only rules, paths and exception types.
/// </param>
internal sealed class MessageHandler(
    Func<Settings> settings,
    DeliveryLedger ledger,
    PairingStore store,
    SenderHashes hashes,
    WorkFolders work,
    Func<DateTimeOffset> now,
    Action<DirectoryInfo> reveal,
    Action<string> log)
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
            store.Purge(now());
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
        "process" => [("state", Name(Process(MailOf(request), Strings(request, "passwords"))))],
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
            return WaitOrPair(new WaitingZip(KeyOf(mail), file.FullName, sha256, FolderFor(current.Rules, mail).FullName, PairingState.Waiting));
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

    /// <summary>
    /// Records an encrypted ZIP as waiting, then opens it with the candidates of the password mails it pairs with, if any
    /// have arrived (F-3.2, F-3.6). Those candidates are dropped once tried.
    /// </summary>
    private DeliveryState WaitOrPair(WaitingZip zip)
    {
        store.AddZip(zip);
        var waiting = store.PasswordsFor(zip.Mail);
        if (waiting.Count == 0)
        {
            return DeliveryState.Waiting;
        }
        var state = TryOpen(zip, waiting.SelectMany(passwords => passwords.Candidates).Distinct().ToList());
        foreach (var passwords in waiting)
        {
            store.RemovePasswords(passwords.Mail.MessageId);
        }
        return state;
    }

    /// <summary>
    /// Opens the ZIPs a password mail pairs with using its <paramref name="candidates"/>, or, when none is waiting, keeps the
    /// candidates encrypted until a ZIP arrives (F-3.3 to F-3.6). The body is never kept.
    /// </summary>
    /// <returns>The most pressing outcome over the ZIPs tried, or <see cref="DeliveryState.Waiting"/>.</returns>
    private DeliveryState Process(Mail mail, IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0)
        {
            throw new RefusedException("passwords is empty.");
        }
        var key = KeyOf(mail);
        var zips = store.ZipsFor(key);
        if (zips.Count == 0)
        {
            store.AddPasswords(key, candidates);
            return DeliveryState.Waiting;
        }
        return zips.Select(zip => TryOpen(zip, candidates)).MinBy(Urgency);
    }

    /// <summary>
    /// Checks that the waiting ZIP is still the one recorded (F-3.3, F-3.4), then tries <paramref name="candidates"/> in turn
    /// until one delivers it (F-3.5). Each failed try leaves nothing behind (F-4.8). A failure after the ZIP opened is not
    /// a wrong password: it is thrown, and the ZIP keeps waiting.
    /// </summary>
    private DeliveryState TryOpen(WaitingZip zip, IReadOnlyList<string> candidates)
    {
        FileInfo file;
        try
        {
            file = DownloadPath.Accept(zip.Path, settings().DownloadFolder);
        }
        catch (OutsideDownloadFolderException)
        {
            return Mismatch(zip);
        }
        if (Convert.ToHexStringLower(NumberedCopy.Sha256Of(file)) != zip.Sha256)
        {
            return Mismatch(zip);
        }
        var folder = new DirectoryInfo(zip.Folder);
        var created = CreateMissing(folder);
        foreach (var candidate in candidates)
        {
            ZipDeliveryResult result;
            try
            {
                result = ZipDelivery.Deliver(file, folder, candidate, work);
            }
            catch (DeliveryCopyException)
            {
                RemoveIfEmpty(created);
                throw;
            }
            catch (Exception)
            {
                continue;
            }
            ledger.Record(new Delivery(zip.Mail.MessageId, zip.Sha256, folder.FullName, [.. result.Written, .. result.Found], result.Blocked is not null));
            store.RemoveZip(zip.Mail.MessageId, zip.Sha256);
            return ledger.StateOf(zip.Mail.MessageId);
        }
        RemoveIfEmpty(created);
        store.SetState(zip.Mail.MessageId, zip.Sha256, PairingState.ExtractFailed);
        return DeliveryState.ExtractFailed;
    }

    private DeliveryState Mismatch(WaitingZip zip)
    {
        store.SetState(zip.Mail.MessageId, zip.Sha256, PairingState.Mismatch);
        return DeliveryState.Mismatch;
    }

    /// <summary>The badge of a mail: the most pressing of what the ledger and the waiting records know of it.</summary>
    private DeliveryState StateOf(string messageId) => new[] { ledger.StateOf(messageId), store.StateOf(messageId) }.MinBy(Urgency);

    /// <summary>Lower is more pressing for the user: a blocked ZIP, then a ZIP that cannot be opened, then one still waiting, then a delivery.</summary>
    private static int Urgency(DeliveryState state) => state switch
    {
        DeliveryState.Blocked => 0,
        DeliveryState.Mismatch => 1,
        DeliveryState.ExtractFailed => 2,
        DeliveryState.Waiting => 3,
        DeliveryState.Delivered => 4,
        _ => 5,
    };

    private MailKey KeyOf(Mail mail) => new(
        mail.MessageId, mail.ConversationId, hashes.OfAddress(mail.From), hashes.OfDomain(mail.From[(mail.From.LastIndexOf('@') + 1)..]), mail.Metadata.Received);

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
            states[messageId] = Name(StateOf(messageId));
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

    private static IReadOnlyList<string> Strings(JsonObject node, string name) =>
        (node[name] as JsonArray ?? throw new RefusedException($"{name} is missing."))
            .Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw new RefusedException($"{name} must hold strings."))
            .ToList();

    private static Mail MailOf(JsonObject request)
    {
        var mail = request["mail"] as JsonObject ?? throw new RefusedException("mail is missing.");
        var from = Text(mail, "from");
        return new Mail(
            Text(mail, "messageId"),
            Text(mail, "conversationId"),
            from,
            new MailMetadata(from, Text(mail, "subject"), DateTimeOffset.Parse(Text(mail, "received"), System.Globalization.CultureInfo.InvariantCulture)));
    }

    private sealed record Mail(string MessageId, string ConversationId, string From, MailMetadata Metadata);

    /// <summary>A request answered with <c>ok: false</c> and this message as its <c>error</c>.</summary>
    private sealed class RefusedException(string message) : Exception(message);
}
