namespace Ppapd.ZipExtraction;

/// <summary>Delivers one received ZIP into its delivery folder (docs/PRD.md, F-4).</summary>
public static class ZipDelivery
{
    /// <summary>
    /// Extracts <paramref name="zip"/> into <paramref name="destination"/>. When a password is given, also places there a
    /// copy without the password under the name of <paramref name="zip"/> (F-4.4). When the ZIP is blocked (F-4.3, F-4.6),
    /// writes only <c>&lt;ZIP name&gt;.blocked.txt</c> (F-4.7) and does not repack it.
    /// </summary>
    /// <param name="zip">The ZIP in the download folder.</param>
    /// <param name="destination">An existing folder to deliver into.</param>
    /// <param name="password">The password of an encrypted ZIP; <c>null</c> for an unencrypted one.</param>
    /// <returns>Why the ZIP was blocked, or <c>null</c> when it was delivered.</returns>
    /// <exception cref="Exception">
    /// Extraction or repacking failed for another reason, such as a wrong password, corrupt data, or a file of the
    /// repacked ZIP's name already in <paramref name="destination"/>. Whatever this call wrote has been removed and what
    /// was there before is kept (F-4.8).
    /// </exception>
    public static BlockReason? Deliver(FileInfo zip, DirectoryInfo destination, string? password)
    {
        List<FileSystemInfo> written;
        using (var source = zip.OpenRead())
        {
            try
            {
                written = ZipExtractor.Extract(source, destination, password, ExtractionLimits.Default);
            }
            catch (ZipSlipException)
            {
                return Block(zip, destination, BlockReason.ZipSlip);
            }
            catch (ZipTooLargeException)
            {
                return Block(zip, destination, BlockReason.TooLarge);
            }
        }
        if (password is not null)
        {
            try
            {
                Repack(zip, destination, password, written);
            }
            catch
            {
                ZipExtractor.Remove(written);
                throw;
            }
        }
        return null;
    }

    private static BlockReason Block(FileInfo zip, DirectoryInfo destination, BlockReason reason)
    {
        BlockedNotice.Write(destination, zip.Name, reason);
        return reason;
    }

    /// <summary>Writes the copy without the password, adding it to <paramref name="written"/> once created.</summary>
    private static void Repack(FileInfo zip, DirectoryInfo destination, string password, List<FileSystemInfo> written)
    {
        var target = new FileInfo(Path.Combine(destination.FullName, zip.Name));
        using var output = new FileStream(target.FullName, FileMode.CreateNew);
        written.Add(target);
        using var source = zip.OpenRead();
        ZipRepacker.Repack(source, password, output);
    }
}
