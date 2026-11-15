using SharpCompress.Archives.Zip;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Text;

namespace Ppapd.ZipExtraction;

/// <summary>Extracts ZIP archives into a delivery folder (docs/PRD.md, F-4).</summary>
public static class ZipExtractor
{
    /// <summary>The code page Japanese Windows uses for entry names stored without the UTF-8 flag (F-4.2).</summary>
    private static readonly Encoding Cp932 = CodePagesEncodingProvider.Instance.GetEncoding(932)!;

    /// <summary>
    /// Writes every file entry of <paramref name="zip"/> under <paramref name="destination"/>,
    /// keeping the folder structure stored in the archive.
    /// </summary>
    /// <param name="zip">A readable stream positioned at the start of the archive.</param>
    /// <param name="destination">An existing folder to write into.</param>
    public static void Extract(Stream zip, DirectoryInfo destination)
    {
        using var archive = ZipArchive.OpenArchive(zip, new ReaderOptions
        {
            ArchiveEncoding = new ArchiveEncoding { Default = Cp932 },
        });
        var options = new ExtractionOptions(extractFullPath: true, overwrite: false);
        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
        {
            entry.WriteToDirectory(destination.FullName, options);
        }
    }
}
