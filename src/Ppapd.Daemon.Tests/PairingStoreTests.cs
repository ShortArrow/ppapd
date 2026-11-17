using System.Text;
using Microsoft.Data.Sqlite;

namespace Ppapd.Daemon.Tests;

public sealed class PairingStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 12, 12, 0, 0, TimeSpan.FromHours(9));

    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("ppapd-test-");

    private string DatabasePath => Path.Combine(temp.FullName, "ppapd.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        temp.Delete(recursive: true);
    }

    private static MailKey Key(string messageId, string conversation = "c1", string sender = "s1", string domain = "d1", DateTimeOffset? received = null) =>
        new(messageId, conversation, sender, domain, received ?? Noon);

    private static WaitingZip Zip(MailKey mail, string sha256 = "aa") =>
        new(mail, @"C:\Downloads\ppapd\ppap.zip", sha256, @"D:\Projects\P-1\ExampleCorp\20261112", PairingState.Waiting);

    [Fact]
    public void A_waiting_zip_is_found_for_a_password_mail_of_the_same_conversation()
    {
        using var store = new PairingStore(DatabasePath);
        store.AddZip(Zip(Key("<zip>")));

        var found = store.ZipsFor(Key("<pw>", sender: "other", domain: "other", received: Noon.AddHours(23)));

        Assert.Equal(["<zip>"], found.Select(zip => zip.Mail.MessageId));
    }

    [Fact]
    public void Without_the_same_conversation_the_same_sender_within_24_hours_pairs()
    {
        using var store = new PairingStore(DatabasePath);
        store.AddZip(Zip(Key("<zip>", conversation: "c1")));

        Assert.Single(store.ZipsFor(Key("<pw>", conversation: "c2", received: Noon.AddHours(24))));
        Assert.Empty(store.ZipsFor(Key("<pw>", conversation: "c2", received: Noon.AddHours(24).AddMinutes(1))));
    }

    [Fact]
    public void The_same_organization_alone_pairs_only_within_one_hour()
    {
        using var store = new PairingStore(DatabasePath);
        store.AddZip(Zip(Key("<zip>", conversation: "c1", sender: "s1")));

        Assert.Single(store.ZipsFor(Key("<pw>", conversation: "c2", sender: "s2", received: Noon.AddMinutes(-60))));
        Assert.Empty(store.ZipsFor(Key("<pw>", conversation: "c2", sender: "s2", received: Noon.AddMinutes(61))));
        Assert.Empty(store.ZipsFor(Key("<pw>", conversation: "c2", sender: "s2", domain: "d2")));
    }

    [Fact]
    public void A_nearer_tier_wins_over_a_farther_one()
    {
        using var store = new PairingStore(DatabasePath);
        store.AddZip(Zip(Key("<same-conversation>", conversation: "c1", sender: "s9")));
        store.AddZip(Zip(Key("<same-sender>", conversation: "c9", sender: "s1"), sha256: "bb"));

        var found = store.ZipsFor(Key("<pw>", conversation: "c1", sender: "s1"));

        Assert.Equal(["<same-conversation>"], found.Select(zip => zip.Mail.MessageId));
    }

    [Fact]
    public void A_zip_in_mismatch_is_not_offered_again()
    {
        using var store = new PairingStore(DatabasePath);
        store.AddZip(Zip(Key("<zip>")));

        store.SetState("<zip>", "aa", PairingState.Mismatch);

        Assert.Empty(store.ZipsFor(Key("<pw>")));
        Assert.Equal(DeliveryState.Mismatch, store.StateOf("<zip>"));
    }

    [Fact]
    public void A_zip_whose_passwords_all_failed_is_offered_again_to_a_new_password_mail()
    {
        using var store = new PairingStore(DatabasePath);
        store.AddZip(Zip(Key("<zip>")));

        store.SetState("<zip>", "aa", PairingState.ExtractFailed);

        Assert.Single(store.ZipsFor(Key("<pw>")));
        Assert.Equal(DeliveryState.ExtractFailed, store.StateOf("<zip>"));
    }

    [Fact]
    public void Password_candidates_are_found_for_a_zip_mail_and_kept_only_encrypted()
    {
        using (var store = new PairingStore(DatabasePath))
        {
            store.AddPasswords(Key("<pw>"), ["Sample-Pass-2026", "別の候補"]);

            Assert.Equal(["Sample-Pass-2026", "別の候補"], store.PasswordsFor(Key("<zip>")).Single().Candidates);
            Assert.Equal(DeliveryState.Waiting, store.StateOf("<pw>"));
        }

        var raw = File.ReadAllBytes(DatabasePath);
        Assert.Equal(-1, raw.AsSpan().IndexOf("Sample-Pass-2026"u8));
        Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.Unicode.GetBytes("Sample-Pass-2026")));
    }

    [Fact]
    public void Removing_records_leaves_the_mail_in_no_pairing_state()
    {
        using var store = new PairingStore(DatabasePath);
        store.AddZip(Zip(Key("<zip>")));
        store.AddPasswords(Key("<pw>"), ["x"]);

        store.RemoveZip("<zip>", "aa");
        store.RemovePasswords("<pw>");

        Assert.Equal(DeliveryState.None, store.StateOf("<zip>"));
        Assert.Equal(DeliveryState.None, store.StateOf("<pw>"));
    }

    [Fact]
    public void Records_older_than_24_hours_are_purged_and_newer_ones_kept()
    {
        using var store = new PairingStore(DatabasePath);
        store.AddZip(Zip(Key("<old-zip>", received: Noon)));
        store.AddPasswords(Key("<old-pw>", received: Noon), ["x"]);
        store.AddZip(Zip(Key("<new-zip>", received: Noon.AddHours(1)), sha256: "bb"));

        store.Purge(now: Noon.AddHours(24).AddMinutes(1));

        Assert.Equal(DeliveryState.None, store.StateOf("<old-zip>"));
        Assert.Equal(DeliveryState.None, store.StateOf("<old-pw>"));
        Assert.Equal(DeliveryState.Waiting, store.StateOf("<new-zip>"));
    }

    [Fact]
    public void Waiting_records_survive_closing_and_reopening()
    {
        using (var store = new PairingStore(DatabasePath))
        {
            store.AddZip(Zip(Key("<zip>")));
        }

        using var reopened = new PairingStore(DatabasePath);

        Assert.Equal(Zip(Key("<zip>")), reopened.ZipsFor(Key("<pw>")).Single());
    }
}
