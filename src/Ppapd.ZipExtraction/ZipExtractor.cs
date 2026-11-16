using SharpCompress.Archives.Zip;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Crypto;
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
    /// <remarks>
    /// Whenever this call throws, whatever it wrote has been removed and what was in <paramref name="destination"/>
    /// before is kept (F-4.8), so the same archive can be extracted again into the same folder.
    /// </remarks>
    /// <param name="zip">A readable stream positioned at the start of the archive.</param>
    /// <param name="destination">An existing folder to write into.</param>
    /// <param name="password">The password of an encrypted archive; <c>null</c> for an unencrypted one.</param>
    /// <exception cref="ZipSlipException">
    /// An entry name points outside <paramref name="destination"/>; nothing has been written.
    /// </exception>
    /// <exception cref="ZipTooLargeException">
    /// The archive holds more than 10,000 entries, or its files add up to more than 2 GiB as actually extracted.
    /// </exception>
    public static void Extract(Stream zip, DirectoryInfo destination, string? password = null) =>
        _ = Extract(zip, destination, password, ExtractionLimits.Default);

    /// <summary>
    /// <see cref="Extract(Stream, DirectoryInfo, string?)"/> under <paramref name="limits"/> in place of
    /// <see cref="ExtractionLimits.Default"/>.
    /// </summary>
    /// <returns>The files and folders this call created, in the order created.</returns>
    internal static List<FileSystemInfo> Extract(Stream zip, DirectoryInfo destination, string? password, ExtractionLimits limits)
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

        var created = new List<FileSystemInfo>();
        try
        {
            var budget = new ByteBudget(limits.MaxTotalBytes);
            foreach (var entry in entries.Where(entry => !entry.IsDirectory))
            {
                var file = TargetOf(entry.Key ?? string.Empty, destination);
                CreateMissingFolders(file.Directory!, created);
                using var output = new FileStream(file.FullName, FileMode.CreateNew);
                created.Add(file);
                WriteVerified(entry, budget.Guard(output));
            }
        }
        catch
        {
            Remove(created);
            throw;
        }
        return created;
    }

    /// <summary>Deletes <paramref name="created"/>, given in the order created, newest first so each folder is empty when reached.</summary>
    internal static void Remove(IEnumerable<FileSystemInfo> created)
    {
        foreach (var info in created.Reverse())
        {
            info.Delete();
        }
    }

    /// <summary>
    /// The file an entry named <paramref name="name"/> is written to. The folder part is resolved on its own, as
    /// SharpCompress's <c>WriteToDirectory</c> does, so that Windows trims parts such as <c>.. </c> the way F-4.3 was tested.
    /// </summary>
    private static FileInfo TargetOf(string name, DirectoryInfo destination)
    {
        var folder = Path.GetFullPath(Path.Combine(destination.FullName, Path.GetDirectoryName(name) ?? string.Empty));
        return new FileInfo(Path.Combine(folder, Path.GetFileName(name)));
    }

    /// <summary>
    /// Writes the content of <paramref name="entry"/> to <paramref name="output"/> and checks it against the CRC-32
    /// the archive records, as SharpCompress's <c>WriteToDirectory</c> does. An entry recording 0 is not checked,
    /// because AES-encrypted entries in the AE-2 form record 0 in place of their CRC-32.
    /// </summary>
    /// <exception cref="InvalidFormatException">The content does not match the recorded CRC-32.</exception>
    private static void WriteVerified(IArchiveEntry entry, Stream output)
    {
        using var crc = new Crc32Stream(output);
        entry.WriteTo(crc);
        if (entry.Crc != 0 && crc.Crc != entry.Crc)
        {
            throw new InvalidFormatException($"CRC mismatch for entry '{entry.Key}'.");
        }
    }

    /// <summary>Creates <paramref name="folder"/> and its missing parents, adding each one made to <paramref name="created"/>, outermost first.</summary>
    private static void CreateMissingFolders(DirectoryInfo folder, List<FileSystemInfo> created)
    {
        var missing = new Stack<DirectoryInfo>();
        for (var current = folder; current is { Exists: false }; current = current.Parent)
        {
            missing.Push(current);
        }
        foreach (var directory in missing)
        {
            directory.Create();
            created.Add(directory);
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
