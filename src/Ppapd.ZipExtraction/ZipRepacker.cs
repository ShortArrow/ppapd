using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Ppapd.ZipExtraction;

/// <summary>Re-packs a password-protected ZIP without its password (docs/PRD.md, F-4.4).</summary>
public static class ZipRepacker
{
    /// <summary>
    /// Writes to <paramref name="output"/> a ZIP without a password that holds every file of
    /// <paramref name="encryptedZip"/> under the same folders, names them in UTF-8 with the UTF-8 flag,
    /// and carries the SHA-256 of <paramref name="encryptedZip"/> in lowercase hexadecimal as its comment.
    /// </summary>
    /// <remarks>
    /// Precondition: <paramref name="encryptedZip"/> has already passed <see cref="ZipExtractor.Extract"/>
    /// without a <see cref="ZipSlipException"/>. This method does not check entry names for Zip Slip
    /// (F-4.3) and copies them into the new archive as they are.
    /// </remarks>
    /// <param name="encryptedZip">A readable stream positioned at the start of the encrypted archive.</param>
    /// <param name="password">The password of <paramref name="encryptedZip"/>.</param>
    /// <param name="output">A writable stream to receive the new archive.</param>
    public static void Repack(Stream encryptedZip, string password, Stream output)
    {
        using var source = new MemoryStream();
        encryptedZip.CopyTo(source);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(source.GetBuffer().AsSpan(0, (int)source.Length)));
        source.Position = 0;

        using var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8)
        {
            Comment = sha256,
        };
        foreach (var (name, open) in ZipExtractor.FileEntries(source, password))
        {
            using var content = open();
            using var entry = target.CreateEntry(name).Open();
            content.CopyTo(entry);
        }
    }
}
