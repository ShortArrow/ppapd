using System.Security.Cryptography;

namespace Ppapd.Daemon.Tests;

public sealed class SenderHashesTests : IDisposable
{
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("ppapd-test-");

    private string KeyPath => Path.Combine(temp.FullName, "hmac.key");

    public void Dispose() => temp.Delete(recursive: true);

    [Fact]
    public void The_key_is_made_on_first_use_and_the_same_key_is_read_afterwards()
    {
        var first = SenderHashes.LoadOrCreate(KeyPath);

        var again = SenderHashes.LoadOrCreate(KeyPath);

        Assert.True(File.Exists(KeyPath));
        Assert.Equal(first.OfAddress("taro@example.co.jp"), again.OfAddress("taro@example.co.jp"));
    }

    [Fact]
    public void The_key_file_holds_the_key_only_as_dpapi_protects_it_for_the_current_user()
    {
        SenderHashes.LoadOrCreate(KeyPath);

        var stored = File.ReadAllBytes(KeyPath);
        var key = ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser);

        Assert.Equal(32, key.Length);
        Assert.NotEqual(key, stored);
    }

    [Fact]
    public void A_hash_is_the_hmac_sha256_of_the_address_under_the_key_and_ignores_letter_case()
    {
        var hashes = SenderHashes.LoadOrCreate(KeyPath);
        var key = ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser);

        Assert.Equal(
            Convert.ToHexStringLower(HMACSHA256.HashData(key, "taro@example.co.jp"u8)),
            hashes.OfAddress("Taro@Example.co.jp"));
    }

    [Fact]
    public void Addresses_and_domains_are_hashed_apart_and_a_domain_ignores_letter_case()
    {
        var hashes = SenderHashes.LoadOrCreate(KeyPath);

        Assert.NotEqual(hashes.OfAddress("taro@example.co.jp"), hashes.OfAddress("hanako@example.co.jp"));
        Assert.Equal(hashes.OfDomain("example.co.jp"), hashes.OfDomain("EXAMPLE.co.jp"));
        Assert.NotEqual(hashes.OfDomain("example.co.jp"), hashes.OfDomain("example.net"));
    }

    [Fact]
    public void Another_key_gives_other_hashes()
    {
        var other = Path.Combine(temp.FullName, "other.key");

        Assert.NotEqual(
            SenderHashes.LoadOrCreate(KeyPath).OfAddress("taro@example.co.jp"),
            SenderHashes.LoadOrCreate(other).OfAddress("taro@example.co.jp"));
    }
}