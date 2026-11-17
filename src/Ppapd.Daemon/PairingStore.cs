using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Ppapd.Daemon;

/// <summary>What pairing knows of a mail; the sender only as its HMACs (docs/PRD.md, S-6).</summary>
/// <param name="MessageId">The mail.</param>
/// <param name="ConversationId">Its conversation.</param>
/// <param name="SenderHash">The HMAC of the sender address.</param>
/// <param name="DomainHash">The HMAC of the sender domain.</param>
/// <param name="Received">When it was received.</param>
internal sealed record MailKey(string MessageId, string ConversationId, string SenderHash, string DomainHash, DateTimeOffset Received);

/// <summary>Where a waiting ZIP stands.</summary>
internal enum PairingState
{
    /// <summary>Waiting for its password (F-3.2).</summary>
    Waiting,

    /// <summary>Every candidate tried so far failed; a new one is still awaited (F-3.5).</summary>
    ExtractFailed,

    /// <summary>The ZIP is gone or changed; it is not tried again (F-3.4).</summary>
    Mismatch,
}

/// <summary>An encrypted ZIP waiting for its password mail (docs/PRD.md, F-3.2).</summary>
/// <param name="Mail">The ZIP mail.</param>
/// <param name="Path">The ZIP under the download folder.</param>
/// <param name="Sha256">Its SHA-256 when it arrived, to check before extracting (F-3.3).</param>
/// <param name="Folder">The delivery folder decided from the ZIP mail when it arrived (F-5.2).</param>
/// <param name="State">Where it stands.</param>
internal sealed record WaitingZip(MailKey Mail, string Path, string Sha256, string Folder, PairingState State);

/// <summary>Password candidates waiting for their ZIP mail (docs/PRD.md, F-3.6).</summary>
internal sealed record WaitingPasswords(MailKey Mail, IReadOnlyList<string> Candidates);

/// <summary>The waiting records of PPAP pairing, in the SQLite file the ledger uses (docs/PRD.md, F-3).</summary>
internal sealed class PairingStore : IDisposable
{
    private readonly SqliteConnection connection;

    /// <summary>Opens the records at <paramref name="databasePath"/>, creating the file and its tables when missing.</summary>
    public PairingStore(string databasePath)
    {
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        Execute("""
            CREATE TABLE IF NOT EXISTS waiting_zips (
                message_id TEXT NOT NULL, conversation_id TEXT NOT NULL, sender TEXT NOT NULL, domain TEXT NOT NULL,
                received INTEGER NOT NULL, path TEXT NOT NULL, sha256 TEXT NOT NULL, folder TEXT NOT NULL, state TEXT NOT NULL,
                PRIMARY KEY (message_id, sha256))
            """);
        Execute("""
            CREATE TABLE IF NOT EXISTS waiting_passwords (
                message_id TEXT NOT NULL PRIMARY KEY, conversation_id TEXT NOT NULL, sender TEXT NOT NULL, domain TEXT NOT NULL,
                received INTEGER NOT NULL, candidates BLOB NOT NULL)
            """);
    }

    public void AddZip(WaitingZip zip) => Execute(
        """
        INSERT OR REPLACE INTO waiting_zips (message_id, conversation_id, sender, domain, received, path, sha256, folder, state)
        VALUES ($message, $conversation, $sender, $domain, $received, $path, $sha, $folder, $state)
        """,
        [.. KeyParameters(zip.Mail), ("$path", zip.Path), ("$sha", zip.Sha256), ("$folder", zip.Folder), ("$state", zip.State.ToString())]);

    /// <summary>Records <paramref name="candidates"/>, encrypted with DPAPI for the current user (S-4).</summary>
    public void AddPasswords(MailKey mail, IReadOnlyList<string> candidates) => Execute(
        """
        INSERT OR REPLACE INTO waiting_passwords (message_id, conversation_id, sender, domain, received, candidates)
        VALUES ($message, $conversation, $sender, $domain, $received, $candidates)
        """,
        [.. KeyParameters(mail), ("$candidates", ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(candidates), null, DataProtectionScope.CurrentUser))]);

    /// <summary>The ZIPs, not in <see cref="PairingState.Mismatch"/>, that a password mail <paramref name="mail"/> pairs with (F-3.1).</summary>
    public IReadOnlyList<WaitingZip> ZipsFor(MailKey mail) =>
        NearestTier(mail, ReadZips().Where(zip => zip.State != PairingState.Mismatch).ToList(), zip => zip.Mail);

    /// <summary>
    /// The password candidates a ZIP mail <paramref name="mail"/> pairs with (F-3.1). Only the records the tier selects are
    /// decrypted, and one DPAPI cannot open is skipped.
    /// </summary>
    public IReadOnlyList<WaitingPasswords> PasswordsFor(MailKey mail)
    {
        var found = new List<WaitingPasswords>();
        foreach (var (key, encrypted) in NearestTier(mail, ReadPasswordRows(), row => row.Key))
        {
            byte[] plain;
            try
            {
                plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                continue;
            }
            found.Add(new WaitingPasswords(key, JsonSerializer.Deserialize<List<string>>(plain)!));
            CryptographicOperations.ZeroMemory(plain);
        }
        return found;
    }

    public void SetState(string messageId, string sha256, PairingState state) => Execute(
        "UPDATE waiting_zips SET state = $state WHERE message_id = $message AND sha256 = $sha",
        [("$state", state.ToString()), ("$message", messageId), ("$sha", sha256)]);

    public void RemoveZip(string messageId, string sha256) => Execute(
        "DELETE FROM waiting_zips WHERE message_id = $message AND sha256 = $sha", [("$message", messageId), ("$sha", sha256)]);

    public void RemovePasswords(string messageId) => Execute(
        "DELETE FROM waiting_passwords WHERE message_id = $message", [("$message", messageId)]);

    /// <summary>Deletes the records of mail received more than <see cref="PairingWindow.KeepWaiting"/> before <paramref name="now"/> (F-3.2, F-3.6).</summary>
    public void Purge(DateTimeOffset now)
    {
        var cutoff = (now - PairingWindow.KeepWaiting).ToUnixTimeMilliseconds();
        Execute("DELETE FROM waiting_zips WHERE received < $cutoff", [("$cutoff", cutoff)]);
        Execute("DELETE FROM waiting_passwords WHERE received < $cutoff", [("$cutoff", cutoff)]);
    }

    /// <summary>
    /// The pairing state of mail <paramref name="messageId"/>, the most pressing of its records; <see cref="DeliveryState.None"/>
    /// without any. No password candidate is decrypted for this (S-4).
    /// </summary>
    public DeliveryState StateOf(string messageId)
    {
        var states = ReadZips().Where(zip => zip.Mail.MessageId == messageId).Select(zip => zip.State switch
        {
            PairingState.Mismatch => DeliveryState.Mismatch,
            PairingState.ExtractFailed => DeliveryState.ExtractFailed,
            _ => DeliveryState.Waiting,
        }).ToList();
        using (var command = Command("SELECT 1 FROM waiting_passwords WHERE message_id = $message", [("$message", messageId)]))
        {
            if (command.ExecuteScalar() is not null)
            {
                states.Add(DeliveryState.Waiting);
            }
        }
        return states.Count == 0 ? DeliveryState.None : states.MinBy(Urgency);
    }

    /// <summary>Lower is more pressing for the user.</summary>
    private static int Urgency(DeliveryState state) => state switch
    {
        DeliveryState.Mismatch => 0,
        DeliveryState.ExtractFailed => 1,
        _ => 2,
    };

    public void Dispose() => connection.Dispose();

    /// <summary>
    /// The records in the first non-empty tier of F-3.1 for <paramref name="mail"/>: the same conversation, then the same
    /// sender, both within <see cref="PairingWindow.SameConversationOrSender"/>; then the same organization within
    /// <see cref="PairingWindow.SameOrganization"/>. Newest first.
    /// </summary>
    private static IReadOnlyList<T> NearestTier<T>(MailKey mail, IReadOnlyList<T> records, Func<T, MailKey> keyOf)
    {
        bool Within(MailKey other, TimeSpan window) => (other.Received - mail.Received).Duration() <= window;
        Func<MailKey, bool>[] tiers =
        [
            other => other.ConversationId == mail.ConversationId && Within(other, PairingWindow.SameConversationOrSender),
            other => other.SenderHash == mail.SenderHash && Within(other, PairingWindow.SameConversationOrSender),
            other => other.DomainHash == mail.DomainHash && Within(other, PairingWindow.SameOrganization),
        ];
        foreach (var tier in tiers)
        {
            var found = records.Where(record => keyOf(record).MessageId != mail.MessageId && tier(keyOf(record)))
                .OrderByDescending(record => keyOf(record).Received).ToList();
            if (found.Count > 0)
            {
                return found;
            }
        }
        return [];
    }

    private List<WaitingZip> ReadZips()
    {
        using var command = Command("SELECT message_id, conversation_id, sender, domain, received, path, sha256, folder, state FROM waiting_zips", []);
        using var reader = command.ExecuteReader();
        var zips = new List<WaitingZip>();
        while (reader.Read())
        {
            zips.Add(new WaitingZip(KeyOf(reader), reader.GetString(5), reader.GetString(6), reader.GetString(7), Enum.Parse<PairingState>(reader.GetString(8))));
        }
        return zips;
    }

    /// <summary>Every waiting password record, its candidates still encrypted.</summary>
    private List<(MailKey Key, byte[] Encrypted)> ReadPasswordRows()
    {
        using var command = Command("SELECT message_id, conversation_id, sender, domain, received, candidates FROM waiting_passwords", []);
        using var reader = command.ExecuteReader();
        var rows = new List<(MailKey, byte[])>();
        while (reader.Read())
        {
            rows.Add((KeyOf(reader), (byte[])reader.GetValue(5)));
        }
        return rows;
    }

    private static MailKey KeyOf(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)));

    private static (string, object)[] KeyParameters(MailKey mail) =>
    [
        ("$message", mail.MessageId), ("$conversation", mail.ConversationId), ("$sender", mail.SenderHash),
        ("$domain", mail.DomainHash), ("$received", mail.Received.ToUnixTimeMilliseconds()),
    ];

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        command.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql, (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        return command;
    }
}
