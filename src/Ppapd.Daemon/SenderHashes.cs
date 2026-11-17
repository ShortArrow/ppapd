using System.Security.Cryptography;
using System.Text;

namespace Ppapd.Daemon;

/// <summary>
/// Sender addresses and domains as HMAC-SHA256, so that the ledger can compare senders without keeping them in plain text
/// (docs/PRD.md, S-6).
/// </summary>
internal sealed class SenderHashes
{
    private readonly byte[] key;

    private SenderHashes(byte[] key) => this.key = key;

    /// <summary>
    /// Reads the key in <paramref name="keyPath"/>, or, on first use, makes a random 32-byte key and keeps it there as DPAPI
    /// protects it for the current user, apart from the ledger.
    /// </summary>
    public static SenderHashes LoadOrCreate(string keyPath)
    {
        if (File.Exists(keyPath))
        {
            return new SenderHashes(ProtectedData.Unprotect(File.ReadAllBytes(keyPath), null, DataProtectionScope.CurrentUser));
        }
        var key = RandomNumberGenerator.GetBytes(32);
        using (var file = new FileStream(keyPath, FileMode.CreateNew))
        {
            file.Write(ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser));
        }
        return new SenderHashes(key);
    }

    /// <summary>The HMAC-SHA256 of <paramref name="address"/> in lowercase, in hexadecimal.</summary>
    public string OfAddress(string address) => Hash(address);

    /// <summary>The HMAC-SHA256 of <paramref name="domain"/> in lowercase, in hexadecimal; never equal to that of an address, which holds an @.</summary>
    public string OfDomain(string domain) => Hash(domain);

    private string Hash(string text) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text.ToLowerInvariant())));
}