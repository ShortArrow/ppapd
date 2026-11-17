namespace Ppapd.ZipExtraction;

/// <summary>Delivers one received ZIP into its delivery folder (docs/PRD.md, F-4).</summary>
public static class ZipDelivery
{
    /// <summary>
    /// Extracts <paramref name="zip"/> and, when a password is given, repacks it without the password under its own name
    /// (F-4.4), both in a new folder of <paramref name="work"/>; then copies the result into <paramref name="destination"/>.
    /// When the ZIP is blocked (F-4.3, F-4.6), writes only <c>&lt;ZIP name&gt;.blocked.txt</c> there (F-4.7).
    /// The work folder is deleted in every case.
    /// </summary>
    /// <param name="zip">The ZIP in the download folder.</param>
    /// <param name="destination">An existing folder to deliver into.</param>
    /// <param name="password">The password of an encrypted ZIP; <c>null</c> for an unencrypted one.</param>
    /// <param name="work">Where the ZIP is extracted and repacked before delivery.</param>
    /// <returns>Why the ZIP was blocked, or <c>null</c> when it was delivered.</returns>
    /// <exception cref="Exception">
    /// Extraction, repacking or copying failed, for instance on a wrong password, corrupt data, or a file of the same
    /// name already in <paramref name="destination"/>. Nothing of the ZIP is left there, and what was there before is kept (F-4.8).
    /// </exception>
    public static BlockReason? Deliver(FileInfo zip, DirectoryInfo destination, string? password, WorkFolders work)
    {
        using var folder = work.Create();
        if (Prepare(zip, folder, password) is { } reason)
        {
            BlockedNotice.Write(destination, zip.Name, reason);
            return reason;
        }
        CopyInto([folder.Directory, folder.Repacked], destination);
        return null;
    }

    /// <summary>Extracts and, given a password, repacks <paramref name="zip"/> into <paramref name="folder"/>.</summary>
    /// <returns>Why the ZIP was blocked, or <c>null</c> when <paramref name="folder"/> holds what to deliver.</returns>
    private static BlockReason? Prepare(FileInfo zip, WorkFolder folder, string? password)
    {
        using (var source = zip.OpenRead())
        {
            try
            {
                ZipExtractor.Extract(source, folder.Directory, password);
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
        if (password is not null)
        {
            using var output = new FileStream(Path.Combine(folder.Repacked.FullName, zip.Name), FileMode.CreateNew);
            using var source = zip.OpenRead();
            ZipRepacker.Repack(source, password, output);
        }
        return null;
    }

    /// <summary>
    /// Copies every file of each of <paramref name="folders"/>, in order, to the same place under <paramref name="destination"/>,
    /// numbering a name already taken there (F-6.3). Copying, unlike moving, lets each file take the permissions of
    /// <paramref name="destination"/> in place of the work folder's.
    /// </summary>
    /// <exception cref="Exception">A copy failed; what this call made under <paramref name="destination"/> has been removed.</exception>
    private static void CopyInto(IEnumerable<DirectoryInfo> folders, DirectoryInfo destination)
    {
        var copied = new List<FileSystemInfo>();
        try
        {
            foreach (var folder in folders)
            {
                foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    var target = new FileInfo(Path.Combine(destination.FullName, Path.GetRelativePath(folder.FullName, file.FullName)));
                    ZipExtractor.CreateMissingFolders(target.Directory!, copied);
                    copied.Add(NumberedCopy.To(file, target));
                }
            }
        }
        catch
        {
            ZipExtractor.Remove(copied);
            throw;
        }
    }
}
