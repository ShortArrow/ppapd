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
    /// <returns>Why the ZIP was blocked, if it was, and the files written and found already there.</returns>
    /// <exception cref="Exception">
    /// Extraction, repacking or copying failed, for instance on a wrong password or corrupt data. Nothing of the ZIP is
    /// left in <paramref name="destination"/>, and what was there before is kept (F-4.8).
    /// </exception>
    public static ZipDeliveryResult Deliver(FileInfo zip, DirectoryInfo destination, string? password, WorkFolders work)
    {
        using var folder = work.Create();
        if (Prepare(zip, folder, password) is { } reason)
        {
            BlockedNotice.Write(destination, zip.Name, reason);
            return new ZipDeliveryResult(reason, [zip.Name + ".blocked.txt"], []);
        }
        var outcomes = CopyInto([(folder.Directory, NumberedCopy.SameContent), (folder.Repacked, SameRepack)], destination);
        return new ZipDeliveryResult(
            null,
            outcomes.Where(outcome => outcome.Copied).Select(outcome => RelativeName(outcome.File, destination)).ToList(),
            outcomes.Where(outcome => !outcome.Copied).Select(outcome => RelativeName(outcome.File, destination)).ToList());
    }

    private static string RelativeName(FileInfo file, DirectoryInfo destination) =>
        Path.GetRelativePath(destination.FullName, file.FullName).Replace(Path.DirectorySeparatorChar, '/');

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
    /// numbering a name already taken there and skipping a file found there already by its folder's sameness test (F-6.3).
    /// Copying, unlike moving, lets each file take the permissions of <paramref name="destination"/> in place of the work folder's.
    /// </summary>
    /// <returns>Where each file was copied, or the same file found in its place.</returns>
    /// <exception cref="Exception">A copy failed; what this call made under <paramref name="destination"/> has been removed.</exception>
    private static List<CopyOutcome> CopyInto(IEnumerable<(DirectoryInfo Folder, Func<FileInfo, FileInfo, bool> Same)> folders, DirectoryInfo destination)
    {
        var copied = new List<FileSystemInfo>();
        var outcomes = new List<CopyOutcome>();
        try
        {
            foreach (var (folder, same) in folders)
            {
                foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    var target = new FileInfo(Path.Combine(destination.FullName, Path.GetRelativePath(folder.FullName, file.FullName)));
                    ZipExtractor.CreateMissingFolders(target.Directory!, copied);
                    var outcome = NumberedCopy.To(file, target, existing => same(file, existing));
                    if (outcome.Copied)
                    {
                        copied.Add(outcome.File);
                    }
                    outcomes.Add(outcome);
                }
            }
            return outcomes;
        }
        catch
        {
            ZipExtractor.Remove(copied);
            throw;
        }
    }

    /// <summary>
    /// Whether <paramref name="existing"/> is a ZIP repacked from the same encrypted ZIP as <paramref name="repacked"/>, told
    /// by the comment F-4.4 sets; a repacked ZIP differs byte for byte each time it is made.
    /// </summary>
    private static bool SameRepack(FileInfo repacked, FileInfo existing) =>
        CommentOf(existing) is { Length: > 0 } comment && comment == CommentOf(repacked);

    private static string? CommentOf(FileInfo zip)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(zip.FullName);
            return archive.Comment;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}

/// <summary>What <see cref="ZipDelivery.Deliver"/> did, names relative to the delivery folder with <c>/</c> between folders.</summary>
/// <param name="Blocked">Why the ZIP was blocked, or <c>null</c> when it was delivered.</param>
/// <param name="Written">The files written, the notice alone when blocked.</param>
/// <param name="Found">The files not written because the same content was already there under these names (F-6.3).</param>
public sealed record ZipDeliveryResult(BlockReason? Blocked, IReadOnlyList<string> Written, IReadOnlyList<string> Found);
