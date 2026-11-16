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
    /// <param name="password">The password of an encrypted archive; <c>null</c> for an unencrypted one.</param>
    /// <exception cref="ZipSlipException">
    /// An entry name points outside <paramref name="destination"/>; nothing has been written.
    /// </exception>
    /// <exception cref="ZipTooLargeException">
    /// The archive holds more than 10,000 entries; nothing has been written.
    /// </exception>
    public static void Extract(Stream zip, DirectoryInfo destination, string? password = null) =>
        Extract(zip, destination, password, ExtractionLimits.Default);

    /// <summary>
    /// <see cref="Extract(Stream, DirectoryInfo, string?)"/> under <paramref name="limits"/> in place of
    /// <see cref="ExtractionLimits.Default"/>.
    /// </summary>
    internal static void Extract(Stream zip, DirectoryInfo destination, string? password, ExtractionLimits limits)
    {
        using var archive = Open(zip, password);
        var entries = archive.Entries.ToList();
        if (entries.Count > limits.MaxEntries)
        {
            throw new ZipTooLargeException();
        }
        if (entries.Any(entry => IsZipSlip(entry.Key ?? string.Empty, destination)))
        {
            throw new ZipSlipException();
        }

        var options = new ExtractionOptions(extractFullPath: true, overwrite: false);
        foreach (var entry in entries.Where(entry => !entry.IsDirectory))
        {
            entry.WriteToDirectory(destination.FullName, options);
        }
    }

    /// <summary>
    /// Yields the name, as read under F-4.2, and a content opener of every file entry of <paramref name="zip"/>.
    /// Each opener is valid only until the enumeration moves on.
    /// </summary>
    internal static IEnumerable<(string Name, Func<Stream> Open)> FileEntries(Stream zip, string? password)
    {
        using var archive = Open(zip, password);
        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
        {
            yield return (entry.Key ?? string.Empty, entry.OpenEntryStream);
        }
    }

    private static IArchive Open(Stream zip, string? password) =>
        ZipArchive.OpenArchive(zip, new ReaderOptions
        {
            ArchiveEncoding = new ArchiveEncoding { Default = Cp932 },
            Password = password,
        });

    private static bool IsZipSlip(string name, DirectoryInfo destination) =>
        ClimbsOrIsRooted(name) || ResolvesOutside(name, destination);

    /// <summary>The name forms F-4.3 lists: a <c>..</c> part, or a path starting at a root or a drive.</summary>
    private static bool ClimbsOrIsRooted(string name)
    {
        var slashed = name.Replace('\\', '/');
        return slashed.StartsWith('/')
            || (slashed.Length >= 2 && char.IsAsciiLetter(slashed[0]) && slashed[1] == ':')
            || slashed.Split('/').Contains("..");
    }

    /// <summary>
    /// Whether the entry's full path, as <see cref="Path.GetFullPath(string)"/> resolves it, lies outside
    /// the destination; a second guard for forms the name check does not list.
    /// </summary>
    private static bool ResolvesOutside(string name, DirectoryInfo destination)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination.FullName)) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, name)) + Path.DirectorySeparatorChar;
        return !target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
