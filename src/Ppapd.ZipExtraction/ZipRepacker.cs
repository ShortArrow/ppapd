namespace Ppapd.ZipExtraction;

/// <summary>Re-packs a password-protected ZIP without its password (docs/PRD.md, F-4.4).</summary>
public static class ZipRepacker
{
    /// <summary>
    /// Writes to <paramref name="output"/> a ZIP without a password that holds every file of
    /// <paramref name="encryptedZip"/> under the same folders, names them in UTF-8 with the UTF-8 flag,
    /// and carries the SHA-256 of <paramref name="encryptedZip"/> in lowercase hexadecimal as its comment.
    /// </summary>
    /// <param name="encryptedZip">A readable stream positioned at the start of the encrypted archive.</param>
    /// <param name="password">The password of <paramref name="encryptedZip"/>.</param>
    /// <param name="output">A writable stream to receive the new archive.</param>
    public static void Repack(Stream encryptedZip, string password, Stream output) =>
        throw new NotImplementedException();
}
