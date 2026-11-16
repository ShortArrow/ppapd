namespace Ppapd.ZipExtraction;

/// <summary>How much one ZIP may put into the delivery folder before extraction stops (docs/PRD.md, F-4.6).</summary>
/// <param name="MaxTotalBytes">The most bytes, counted as actually extracted, all file entries together may write.</param>
/// <param name="MaxEntries">The most entries, folders included, the archive may hold.</param>
internal sealed record ExtractionLimits(long MaxTotalBytes, int MaxEntries)
{
    /// <summary>The limits F-4.6 sets: 2 GiB and 10,000 entries.</summary>
    public static ExtractionLimits Default { get; } = new(2L * 1024 * 1024 * 1024, 10_000);
}
