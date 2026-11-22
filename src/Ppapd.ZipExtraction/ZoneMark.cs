namespace Ppapd.ZipExtraction;

/// <summary>
/// The mark Windows keeps on a file that came from the internet: the alternate data stream <c>Zone.Identifier</c>
/// (docs/PRD.md, F-4.9). Office blocks the macros of a file marked as from the internet only while the mark is there.
/// </summary>
public static class ZoneMark
{
    private const string Stream = ":Zone.Identifier";

    /// <summary>The mark on <paramref name="file"/>, or <c>null</c> when it has none.</summary>
    public static byte[]? Of(FileInfo file)
    {
        var stream = file.FullName + Stream;
        return File.Exists(stream) ? File.ReadAllBytes(stream) : null;
    }

    /// <summary>
    /// Gives <paramref name="file"/> exactly <paramref name="mark"/>, replacing any mark it has, or removes its mark when
    /// <paramref name="mark"/> is <c>null</c>.
    /// </summary>
    /// <returns><c>false</c> when the file system or the file refused, for instance a folder not on NTFS; the file is then as it was.</returns>
    public static bool Apply(FileInfo file, byte[]? mark)
    {
        var stream = file.FullName + Stream;
        try
        {
            if (mark is null)
            {
                File.Delete(stream);
            }
            else
            {
                File.WriteAllBytes(stream, mark);
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
