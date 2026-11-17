using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Ppapd.ZipExtraction;

namespace Ppapd.Daemon.Tests;

public sealed class MessageHandlerTests : IDisposable
{
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("ppapd-test-");
    private readonly DirectoryInfo downloads;
    private readonly DirectoryInfo projects;
    private readonly DeliveryLedger ledger;
    private readonly PairingStore store;
    private readonly SenderHashes hashes;
    private readonly List<DirectoryInfo> revealed = [];
    private DateTimeOffset now = Received.AddHours(1);
    private readonly List<string> log = [];
    private Func<Settings> settings;

    public MessageHandlerTests()
    {
        downloads = temp.CreateSubdirectory("downloads");
        projects = temp.CreateSubdirectory("projects");
        ledger = new DeliveryLedger(Path.Combine(temp.FullName, "ppapd.db"));
        store = new PairingStore(Path.Combine(temp.FullName, "ppapd.db"));
        hashes = SenderHashes.LoadOrCreate(Path.Combine(temp.FullName, "hmac.key"));
        var rules = new DeliveryRules(
            projects.FullName,
            "_Unsorted",
            [new SenderAlias("example.co.jp", null, "ExampleCorp")],
            [new ProjectRule("{Capture:1}", "example.co.jp", "【(P-\\d{5})】")]);
        settings = () => new Settings(rules, downloads);
    }

    private static readonly DateTimeOffset Received = new(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Local));

    private MessageHandler Handler => new(
        settings, ledger, store, hashes, new WorkFolders(temp.CreateSubdirectory("work")), () => now, revealed.Add, log.Add);

    private DirectoryInfo DeliveryFolder => new(Path.Combine(projects.FullName, "P-12345", "ExampleCorp", "20261112"));

    public void Dispose()
    {
        ledger.Dispose();
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        temp.Delete(recursive: true);
    }

    [Fact]
    public void A_reply_carries_the_seq_of_its_request()
    {
        var reply = Handler.Handle(new JsonObject { ["type"] = "status", ["seq"] = 42, ["messageIds"] = new JsonArray() });

        Assert.Equal(42, (int)reply["seq"]!);
        Assert.True((bool)reply["ok"]!);
    }

    [Theory]
    [InlineData("【P-12345】図面送付", true)]
    [InlineData("ご挨拶", false)]
    public void Match_tells_whether_a_project_rule_matches_the_mail(string subject, bool matches)
    {
        var reply = Handler.Handle(Request("match", ("mail", Mail(subject: subject))));

        Assert.True((bool)reply["ok"]!);
        Assert.Equal(matches, (bool)reply["matches"]!);
    }

    [Fact]
    public void Downloading_a_file_delivers_it_to_the_folder_its_mail_decides_and_records_it()
    {
        var path = SaveDownload("図面.pdf", "pdf\n");

        var reply = Handler.Handle(Request("download", ("mail", Mail()), ("path", path)));

        Assert.Equal("delivered", (string)reply["state"]!);
        Assert.Equal("pdf\n", File.ReadAllText(Path.Combine(DeliveryFolder.FullName, "図面.pdf")));
        Assert.Equal(["図面.pdf"], ledger.DeliveriesOf("<m1@example.co.jp>").Single().FileNames);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Downloading_the_same_file_of_the_same_mail_again_delivers_nothing_more()
    {
        var path = SaveDownload("図面.pdf", "pdf\n");
        Handler.Handle(Request("download", ("mail", Mail()), ("path", path)));

        var reply = Handler.Handle(Request("download", ("mail", Mail()), ("path", path)));

        Assert.Equal("delivered", (string)reply["state"]!);
        Assert.Equal(["図面.pdf"], DeliveryFolder.EnumerateFiles().Select(file => file.Name));
        Assert.Single(ledger.DeliveriesOf("<m1@example.co.jp>"));
    }

    [Fact]
    public void Downloading_an_unencrypted_zip_delivers_what_it_holds()
    {
        var path = SaveZip("図面一式.zip", "図面/A-001.txt");

        var reply = Handler.Handle(Request("download", ("mail", Mail()), ("path", path)));

        Assert.Equal("delivered", (string)reply["state"]!);
        Assert.True(File.Exists(Path.Combine(DeliveryFolder.FullName, "図面", "A-001.txt")));
        Assert.Equal(["図面/A-001.txt"], ledger.DeliveriesOf("<m1@example.co.jp>").Single().FileNames);
    }

    [Fact]
    public void Downloading_a_zip_that_climbs_out_is_blocked_with_its_notice_and_the_mail_shows_blocked()
    {
        var path = SaveZip("見積書.zip", "../outside.txt");

        var reply = Handler.Handle(Request("download", ("mail", Mail()), ("path", path)));

        Assert.Equal("blocked", (string)reply["state"]!);
        Assert.Equal(["見積書.zip.blocked.txt"], DeliveryFolder.EnumerateFiles().Select(file => file.Name));
        Assert.Equal(DeliveryState.Blocked, ledger.StateOf("<m1@example.co.jp>"));
    }

    [Fact]
    public void A_download_outside_the_download_folder_is_refused_and_nothing_is_delivered()
    {
        var outside = Path.Combine(temp.FullName, "secret.txt");
        File.WriteAllText(outside, "secret\n");

        var reply = Handler.Handle(Request("download", ("mail", Mail()), ("path", outside)));

        Assert.False((bool)reply["ok"]!);
        Assert.False(DeliveryFolder.Exists);
    }

    [Fact]
    public void An_encrypted_zip_without_its_password_waits_and_nothing_is_delivered_yet()
    {
        var reply = Handler.Handle(Request("download", ("mail", Mail()), ("path", SaveEncryptedZip())));

        Assert.Equal("waiting", (string)reply["state"]!);
        Assert.False(DeliveryFolder.Exists);
        Assert.Equal(DeliveryState.Waiting, store.StateOf("<m1@example.co.jp>"));
    }

    [Fact]
    public void Status_gives_the_state_of_each_mail()
    {
        Handler.Handle(Request("download", ("mail", Mail()), ("path", SaveDownload("図面.pdf", "pdf\n"))));

        var reply = Handler.Handle(Request("status", ("messageIds", new JsonArray("<m1@example.co.jp>", "<other@example.co.jp>"))));

        Assert.Equal("delivered", (string)reply["states"]!["<m1@example.co.jp>"]!);
        Assert.Equal("none", (string)reply["states"]!["<other@example.co.jp>"]!);
    }

    [Fact]
    public void Reveal_opens_the_delivery_folder_the_ledger_recorded_for_the_mail()
    {
        Handler.Handle(Request("download", ("mail", Mail()), ("path", SaveDownload("図面.pdf", "pdf\n"))));

        var reply = Handler.Handle(Request("reveal", ("messageId", "<m1@example.co.jp>")));

        Assert.True((bool)reply["ok"]!);
        Assert.Equal([DeliveryFolder.FullName], revealed.Select(folder => folder.FullName));
    }

    [Fact]
    public void Reveal_of_a_mail_never_delivered_opens_nothing()
    {
        var reply = Handler.Handle(Request("reveal", ("messageId", "<none@example.co.jp>")));

        Assert.False((bool)reply["ok"]!);
        Assert.Empty(revealed);
    }

    [Fact]
    public void Reveal_of_a_delivery_folder_the_user_has_since_removed_opens_nothing()
    {
        Handler.Handle(Request("download", ("mail", Mail()), ("path", SaveDownload("図面.pdf", "pdf\n"))));
        DeliveryFolder.Delete(recursive: true);

        var reply = Handler.Handle(Request("reveal", ("messageId", "<m1@example.co.jp>")));

        Assert.False((bool)reply["ok"]!);
        Assert.Empty(revealed);
    }

    [Fact]
    public void A_type_the_daemon_does_not_know_is_answered_with_an_error()
    {
        var reply = Handler.Handle(Request("unknown", ("mail", Mail())));

        Assert.False((bool)reply["ok"]!);
        Assert.NotNull((string?)reply["error"]);
    }

    [Fact]
    public void A_request_missing_a_field_is_answered_with_an_error()
    {
        var reply = Handler.Handle(Request("download", ("mail", Mail())));

        Assert.False((bool)reply["ok"]!);
    }

    [Fact]
    public void When_rules_yaml_cannot_be_read_a_message_that_needs_it_is_answered_with_an_error_and_logged()
    {
        settings = () => throw new InvalidRulesException("rules.yaml needs projects_root.");

        var reply = Handler.Handle(Request("match", ("mail", Mail())));

        Assert.False((bool)reply["ok"]!);
        Assert.Contains(log, line => line.Contains("projects_root"));
    }

    [Fact]
    public void A_subject_rule_that_ran_out_of_time_is_logged_without_the_subject_or_the_sender()
    {
        var rules = settings().Rules with { Projects = [new ProjectRule("Runaway", null, "^(a+)+$")] };
        settings = () => new Settings(rules, downloads);
        var subject = new string('a', 40) + "!";

        Handler.Handle(Request("download", ("mail", Mail(subject: subject)), ("path", SaveDownload("図面.pdf", "pdf\n"))));

        Assert.Contains(log, line => line.Contains("^(a+)+$"));
        Assert.DoesNotContain(log, line => line.Contains(subject) || line.Contains("taro@example.co.jp"));
    }

    [Fact]
    public void A_zip_failing_partway_leaves_no_folder_behind_and_is_answered_with_an_error()
    {
        var path = Path.Combine(downloads.FullName, "壊れた.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry("図面/A-001.txt", CompressionLevel.NoCompression).Open();
            stream.Write("sample\n"u8);
        }
        var bytes = File.ReadAllBytes(path);
        var content = bytes.AsSpan().IndexOf("sample\n"u8);
        bytes[content] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var reply = Handler.Handle(Request("download", ("mail", Mail()), ("path", path)));

        Assert.False((bool)reply["ok"]!);
        Assert.Empty(projects.EnumerateFileSystemInfos());
        Assert.Equal(DeliveryState.None, ledger.StateOf("<m1@example.co.jp>"));
    }

    [Fact]
    public void A_subject_rule_that_ran_out_of_time_during_match_is_logged_too()
    {
        var rules = settings().Rules with { Projects = [new ProjectRule("Runaway", null, "^(a+)+$")] };
        settings = () => new Settings(rules, downloads);

        var reply = Handler.Handle(Request("match", ("mail", Mail(subject: new string('a', 40) + "!"))));

        Assert.False((bool)reply["matches"]!);
        Assert.Contains(log, line => line.Contains("^(a+)+$"));
    }
    [Fact]
    public void A_zip_mail_first_then_its_password_mail_delivers_the_zip_with_a_copy_without_the_password()
    {
        Handler.Handle(Request("download", ("mail", Mail()), ("path", SaveEncryptedZip())));

        var reply = Handler.Handle(PasswordMail(passwords: ["Sample-Pass-2026"]));

        Assert.Equal("delivered", (string)reply["state"]!);
        Assert.True(File.Exists(Path.Combine(DeliveryFolder.FullName, "図面", "A-001_平面図.txt")));
        Assert.True(File.Exists(Path.Combine(DeliveryFolder.FullName, "ppap-zipcrypto-utf8.zip")));
        Assert.Equal(DeliveryState.Delivered, Status("<m1@example.co.jp>"));
        Assert.Equal(DeliveryState.None, store.StateOf("<pw@example.co.jp>"));
    }

    [Fact]
    public void A_password_mail_first_waits_and_its_zip_mail_then_delivers_and_the_candidates_are_dropped()
    {
        var waiting = Handler.Handle(PasswordMail(passwords: ["Sample-Pass-2026"]));

        var reply = Handler.Handle(Request("download", ("mail", Mail(received: Received.AddMinutes(2))), ("path", SaveEncryptedZip("ppap-aes256-utf8.zip"))));

        Assert.Equal("waiting", (string)waiting["state"]!);
        Assert.Equal("delivered", (string)reply["state"]!);
        Assert.True(File.Exists(Path.Combine(DeliveryFolder.FullName, "仕様書.txt")));
        Assert.Empty(store.PasswordsFor(new MailKey("<any>", "c1", "", "", Received)));
    }

    [Fact]
    public void Candidates_are_tried_in_turn_until_one_opens_the_zip()
    {
        Handler.Handle(Request("download", ("mail", Mail()), ("path", SaveEncryptedZip())));

        var reply = Handler.Handle(PasswordMail(passwords: ["wrong-1", "wrong-2", "Sample-Pass-2026"]));

        Assert.Equal("delivered", (string)reply["state"]!);
        Assert.Equal(SampleFiles, DeliveredFiles());
    }

    [Fact]
    public void When_every_candidate_fails_the_zip_waits_for_a_new_password_mail_and_leaves_nothing_behind()
    {
        Handler.Handle(Request("download", ("mail", Mail()), ("path", SaveEncryptedZip())));

        var failed = Handler.Handle(PasswordMail(passwords: ["wrong"]));
        var reply = Handler.Handle(PasswordMail(passwords: ["Sample-Pass-2026"]));

        Assert.Equal("extractFailed", (string)failed["state"]!);
        Assert.Equal("delivered", (string)reply["state"]!);
        Assert.Equal(SampleFiles, DeliveredFiles());
    }

    [Fact]
    public void A_zip_changed_while_it_waited_is_not_extracted_and_shows_mismatch()
    {
        var path = SaveEncryptedZip();
        Handler.Handle(Request("download", ("mail", Mail()), ("path", path)));
        File.AppendAllText(path, "tampered");

        var reply = Handler.Handle(PasswordMail(passwords: ["Sample-Pass-2026"]));

        Assert.Equal("mismatch", (string)reply["state"]!);
        Assert.False(DeliveryFolder.Exists);
        Assert.Equal(DeliveryState.Mismatch, Status("<m1@example.co.jp>"));
    }

    [Fact]
    public void A_zip_removed_while_it_waited_shows_mismatch()
    {
        var path = SaveEncryptedZip();
        Handler.Handle(Request("download", ("mail", Mail()), ("path", path)));
        File.Delete(path);

        var reply = Handler.Handle(PasswordMail(passwords: ["Sample-Pass-2026"]));

        Assert.Equal("mismatch", (string)reply["state"]!);
    }

    [Fact]
    public void A_password_mail_past_the_window_does_not_pair_and_the_old_zip_record_is_dropped()
    {
        Handler.Handle(Request("download", ("mail", Mail()), ("path", SaveEncryptedZip())));
        now = Received.AddHours(25);

        var reply = Handler.Handle(PasswordMail(received: Received.AddHours(25), passwords: ["Sample-Pass-2026"]));

        Assert.Equal("waiting", (string)reply["state"]!);
        Assert.False(DeliveryFolder.Exists);
        Assert.Equal(DeliveryState.None, Status("<m1@example.co.jp>"));
    }

    [Fact]
    public void Neither_the_body_nor_a_password_nor_the_sender_address_is_written_to_the_database()
    {
        Handler.Handle(PasswordMail(passwords: ["Sample-Pass-2026"]));
        store.Dispose();
        ledger.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var raw = File.ReadAllBytes(Path.Combine(temp.FullName, "ppapd.db"));

        foreach (var secret in new[] { "お世話になっております", "Sample-Pass-2026", "taro@example.co.jp" })
        {
            Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)));
            Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.Unicode.GetBytes(secret)));
        }
    }

    [Fact]
    public void A_password_mail_without_candidates_is_answered_with_an_error()
    {
        var reply = Handler.Handle(PasswordMail(passwords: []));

        Assert.False((bool)reply["ok"]!);
    }

    private static readonly string[] SampleFiles = ["ppap-zipcrypto-utf8.zip", "readme.txt", "仕様書.txt", "図面/A-001_平面図.txt", "図面/A-002_立面図.txt"];

    private string[] DeliveredFiles() =>
        DeliveryFolder.EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(DeliveryFolder.FullName, file.FullName).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToArray();

    private DeliveryState Status(string messageId) =>
        Enum.Parse<DeliveryState>((string)Handler.Handle(Request("status", ("messageIds", new JsonArray(messageId))))["states"]![messageId]!, ignoreCase: true);
    private static JsonObject Request(string type, params (string Name, JsonNode? Value)[] fields)
    {
        var request = new JsonObject { ["type"] = type, ["seq"] = 1 };
        foreach (var (name, value) in fields)
        {
            request[name] = value;
        }
        return request;
    }

    private static JsonObject Mail(
        string subject = "【P-12345】図面送付",
        string messageId = "<m1@example.co.jp>",
        string conversation = "c1",
        string from = "taro@example.co.jp",
        DateTimeOffset? received = null) => new()
    {
        ["messageId"] = messageId,
        ["conversationId"] = conversation,
        ["from"] = from,
        ["subject"] = subject,
        ["received"] = (received ?? Received).ToString("O"),
    };

    private static JsonObject PasswordMail(string conversation = "c1", DateTimeOffset? received = null, params string[] passwords)
    {
        var request = Request(
            "process",
            ("mail", Mail(subject: "パスワードのお知らせ", messageId: "<pw@example.co.jp>", conversation: conversation, received: received ?? Received.AddMinutes(1))),
            ("body", "お世話になっております。パスワード：" + string.Join(" ", passwords)));
        request["passwords"] = new JsonArray(passwords.Select(password => (JsonNode)password).ToArray());
        return request;
    }

    private string SaveEncryptedZip(string sample = "ppap-zipcrypto-utf8.zip")
    {
        var path = Path.Combine(downloads.FullName, sample);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", sample), path, overwrite: true);
        return path;
    }

    private string SaveDownload(string name, string content)
    {
        var path = Path.Combine(downloads.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string SaveZip(string name, params string[] entries)
    {
        var path = Path.Combine(downloads.FullName, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using var stream = archive.CreateEntry(entry).Open();
            stream.Write(Encoding.UTF8.GetBytes("sample\n"));
        }
        return path;
    }
}
