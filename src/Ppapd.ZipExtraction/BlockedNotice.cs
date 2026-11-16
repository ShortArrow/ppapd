namespace Ppapd.ZipExtraction;

/// <summary>Why a ZIP was not delivered (docs/PRD.md, F-4.7).</summary>
public enum BlockReason
{
    /// <summary>An entry name points outside the delivery folder (F-4.3).</summary>
    ZipSlip,

    /// <summary>The ZIP exceeds the extraction limits (F-4.6).</summary>
    TooLarge,
}

/// <summary>The text file that tells the user a ZIP was not delivered (docs/PRD.md, F-4.7).</summary>
public static class BlockedNotice
{
    /// <summary>Writes <c>&lt;zipName&gt;.blocked.txt</c> into <paramref name="destination"/>.</summary>
    public static void Write(DirectoryInfo destination, string zipName, BlockReason reason) =>
        throw new NotImplementedException();
}
