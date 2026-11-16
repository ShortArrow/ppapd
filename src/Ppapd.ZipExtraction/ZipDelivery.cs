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
    /// Extraction failed for another reason, such as a wrong password or corrupt data; what it wrote has been removed (F-4.8).
    /// </exception>
    public static BlockReason? Deliver(FileInfo zip, DirectoryInfo destination, string? password)
    {
        if (ExtractOrBlock(zip, destination, password) is { } reason)
        {
            BlockedNotice.Write(destination, zip.Name, reason);
            return reason;
        }
        if (password is not null)
        {
            using var source = zip.OpenRead();
            using var output = new FileStream(Path.Combine(destination.FullName, zip.Name), FileMode.CreateNew);
            ZipRepacker.Repack(source, password, output);
        }
        return null;
    }

    private static BlockReason? ExtractOrBlock(FileInfo zip, DirectoryInfo destination, string? password)
    {
        using var source = zip.OpenRead();
        try
        {
            ZipExtractor.Extract(source, destination, password);
            return null;
        }
        catch (ZipSlipException)
        {
            return BlockReason.ZipSlip;
        }
        catch (ZipTooLargeException)
        {
            return BlockReason.TooLarge;
        }
    }
}
