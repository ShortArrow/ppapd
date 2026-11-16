namespace Ppapd.ZipExtraction;

/// <summary>
/// Thrown when a ZIP exceeds the extraction limits on total bytes or number of entries (docs/PRD.md, F-4.6).
/// Nothing from such a ZIP remains in the delivery folder when this is thrown.
/// </summary>
public sealed class ZipTooLargeException : Exception
{
}
