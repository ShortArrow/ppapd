namespace Ppapd.Daemon;

/// <summary>The check on a path the browser extension sends with <c>download</c> (docs/PRD.md, F-5.1 and S-7).</summary>
internal static class DownloadPath
{
    /// <summary>The file <paramref name="path"/> names, when it is an existing file under <paramref name="downloadFolder"/>.</summary>
    /// <exception cref="OutsideDownloadFolderException">
    /// <paramref name="path"/> is relative, resolves outside <paramref name="downloadFolder"/>, names a data stream,
    /// is not an existing file, or passes through a symbolic link or junction under <paramref name="downloadFolder"/>.
    /// </exception>
    public static FileInfo Accept(string path, DirectoryInfo downloadFolder)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new OutsideDownloadFolderException();
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(downloadFolder.FullName)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full[root.Length..].Contains(':'))
        {
            throw new OutsideDownloadFolderException();
        }
        var file = new FileInfo(full);
        if (!file.Exists || PassesThroughLink(file, root))
        {
            throw new OutsideDownloadFolderException();
        }
        return file;
    }

    /// <summary>Whether <paramref name="file"/>, or a folder between it and <paramref name="root"/>, is a symbolic link or junction.</summary>
    private static bool PassesThroughLink(FileInfo file, string root)
    {
        for (FileSystemInfo? current = file; current is not null && current.FullName.Length >= root.Length; current = (current as FileInfo)?.Directory ?? (current as DirectoryInfo)?.Parent)
        {
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }
        return false;
    }
}