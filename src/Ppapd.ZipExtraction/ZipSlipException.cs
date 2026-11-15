namespace Ppapd.ZipExtraction;

/// <summary>
/// Thrown when a ZIP holds an entry whose name points outside the delivery folder (docs/PRD.md, F-4.3).
/// Nothing from such a ZIP has been written when this is thrown.
/// </summary>
public sealed class ZipSlipException : Exception
{
}
