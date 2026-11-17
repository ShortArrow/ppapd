namespace Ppapd.Daemon.Tests;

public sealed class DeliveryLedgerTests : IDisposable
{
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("ppapd-test-");

    private string DatabasePath => Path.Combine(temp.FullName, "ppapd.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        temp.Delete(recursive: true);
    }

    private static readonly Delivery Example = new(
        MessageId: "<a1@example.co.jp>",
        Sha256: "BA4F2CF8" + new string('0', 56),
        Folder: @"\\nas01\Share\Projects\P-12345\ExampleCorp\20261112",
        FileNames: ["図面/A-001_平面図.txt", "仕様書.txt"]);

    [Fact]
    public void A_recorded_delivery_is_found_by_its_message_and_file_hash()
    {
        using var ledger = new DeliveryLedger(DatabasePath);

        ledger.Record(Example);

        Assert.True(ledger.Has(Example.MessageId, Example.Sha256));
    }

    [Fact]
    public void The_same_file_from_another_message_or_another_file_from_the_same_message_is_not_found()
    {
        using var ledger = new DeliveryLedger(DatabasePath);
        ledger.Record(Example);

        Assert.False(ledger.Has("<other@example.co.jp>", Example.Sha256));
        Assert.False(ledger.Has(Example.MessageId, new string('F', 64)));
    }

    [Fact]
    public void The_hash_is_compared_without_regard_to_letter_case()
    {
        using var ledger = new DeliveryLedger(DatabasePath);
        ledger.Record(Example);

        Assert.True(ledger.Has(Example.MessageId, Example.Sha256.ToLowerInvariant()));
    }

    [Fact]
    public void Deliveries_survive_closing_and_reopening_the_ledger()
    {
        using (var ledger = new DeliveryLedger(DatabasePath))
        {
            ledger.Record(Example);
        }

        using var reopened = new DeliveryLedger(DatabasePath);

        Assert.Equal([Example with { Sha256 = Example.Sha256.ToLowerInvariant() }], reopened.DeliveriesOf(Example.MessageId), DeliveryComparer.Instance);
    }

    [Fact]
    public void Every_delivery_of_a_message_is_listed_with_its_folder_and_file_names_in_order()
    {
        using var ledger = new DeliveryLedger(DatabasePath);
        var second = Example with { Sha256 = new string('1', 64), FileNames = ["見積書.pdf"] };
        ledger.Record(Example);
        ledger.Record(second);

        Assert.Equal(
            [Example with { Sha256 = Example.Sha256.ToLowerInvariant() }, second],
            ledger.DeliveriesOf(Example.MessageId),
            DeliveryComparer.Instance);
    }

    [Fact]
    public void A_message_with_no_delivery_lists_nothing()
    {
        using var ledger = new DeliveryLedger(DatabasePath);

        Assert.Empty(ledger.DeliveriesOf("<none@example.co.jp>"));
    }

    [Fact]
    public void A_message_with_no_record_is_in_no_state()
    {
        using var ledger = new DeliveryLedger(DatabasePath);

        Assert.Equal(DeliveryState.None, ledger.StateOf("<none@example.co.jp>"));
    }

    [Fact]
    public void A_message_whose_files_were_delivered_is_delivered()
    {
        using var ledger = new DeliveryLedger(DatabasePath);
        ledger.Record(Example);

        Assert.Equal(DeliveryState.Delivered, ledger.StateOf(Example.MessageId));
    }

    [Fact]
    public void A_message_with_a_blocked_zip_is_blocked_even_when_its_other_files_were_delivered()
    {
        using var ledger = new DeliveryLedger(DatabasePath);
        ledger.Record(Example);
        ledger.Record(Example with { Sha256 = new string('2', 64), FileNames = ["見積書.zip.blocked.txt"], Blocked = true });

        Assert.Equal(DeliveryState.Blocked, ledger.StateOf(Example.MessageId));
        Assert.True(ledger.DeliveriesOf(Example.MessageId)[1].Blocked);
    }

    /// <summary>Compares deliveries by value, file names included, since a record compares lists by reference.</summary>
    private sealed class DeliveryComparer : IEqualityComparer<Delivery>
    {
        public static readonly DeliveryComparer Instance = new();

        public bool Equals(Delivery? x, Delivery? y) =>
            x is not null && y is not null
            && x.MessageId == y.MessageId && x.Sha256 == y.Sha256 && x.Folder == y.Folder && x.Blocked == y.Blocked
            && x.FileNames.SequenceEqual(y.FileNames);

        public int GetHashCode(Delivery delivery) => HashCode.Combine(delivery.MessageId, delivery.Sha256);
    }
}
