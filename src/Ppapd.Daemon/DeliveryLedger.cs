using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Ppapd.Daemon;

/// <summary>One delivery as the ledger keeps it (docs/PRD.md, F-6.4).</summary>
/// <param name="MessageId">The mail the file came with.</param>
/// <param name="Sha256">The SHA-256 of the received file, in hexadecimal.</param>
/// <param name="Folder">The delivery folder.</param>
/// <param name="FileNames">The files written, relative to <paramref name="Folder"/>.</param>
internal sealed record Delivery(string MessageId, string Sha256, string Folder, IReadOnlyList<string> FileNames);

/// <summary>The delivery ledger in SQLite (docs/PRD.md, F-6.4 and F-6.5).</summary>
internal sealed class DeliveryLedger : IDisposable
{
    private readonly SqliteConnection connection;

    /// <summary>Opens the ledger at <paramref name="databasePath"/>, creating the file and its table when missing.</summary>
    public DeliveryLedger(string databasePath)
    {
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        Execute("""
            CREATE TABLE IF NOT EXISTS deliveries (
                message_id TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                folder TEXT NOT NULL,
                file_names TEXT NOT NULL,
                PRIMARY KEY (message_id, sha256))
            """);
    }

    /// <summary>Records <paramref name="delivery"/>, its hash in lowercase.</summary>
    /// <exception cref="SqliteException">The same file from the same mail is already recorded; F-6.5 has the caller check <see cref="Has"/> first.</exception>
    public void Record(Delivery delivery) => Execute(
        "INSERT INTO deliveries (message_id, sha256, folder, file_names) VALUES ($message, $sha, $folder, $names)",
        ("$message", delivery.MessageId),
        ("$sha", delivery.Sha256.ToLowerInvariant()),
        ("$folder", delivery.Folder),
        ("$names", JsonSerializer.Serialize(delivery.FileNames)));

    /// <summary>Whether the file of hash <paramref name="sha256"/> from mail <paramref name="messageId"/> was delivered (F-6.5).</summary>
    public bool Has(string messageId, string sha256)
    {
        using var command = Command(
            "SELECT 1 FROM deliveries WHERE message_id = $message AND sha256 = $sha",
            ("$message", messageId), ("$sha", sha256.ToLowerInvariant()));
        return command.ExecuteScalar() is not null;
    }

    /// <summary>Every delivery of mail <paramref name="messageId"/>, in the order recorded.</summary>
    public IReadOnlyList<Delivery> DeliveriesOf(string messageId)
    {
        using var command = Command(
            "SELECT sha256, folder, file_names FROM deliveries WHERE message_id = $message ORDER BY rowid",
            ("$message", messageId));
        using var reader = command.ExecuteReader();
        var deliveries = new List<Delivery>();
        while (reader.Read())
        {
            deliveries.Add(new Delivery(
                messageId, reader.GetString(0), reader.GetString(1),
                JsonSerializer.Deserialize<List<string>>(reader.GetString(2))!));
        }
        return deliveries;
    }

    public void Dispose() => connection.Dispose();

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        command.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql, params (string Name, object Value)[] parameters)
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