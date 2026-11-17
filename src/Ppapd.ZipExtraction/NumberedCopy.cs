namespace Ppapd.ZipExtraction;

/// <summary>Copies a file without overwriting, numbering its name when taken (docs/PRD.md, F-6.3).</summary>
public static class NumberedCopy
{
    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="target"/>, or, when that name is taken, to the first free of
    /// <c>name_1.ext</c>, <c>name_2.ext</c>, and so on in the same folder.
    /// </summary>
    /// <returns>The file written.</returns>
    public static FileInfo To(FileInfo source, FileInfo target)
    {
        for (var number = 0; ; number++)
        {
            var candidate = number == 0 ? target : new FileInfo(Path.Combine(
                target.DirectoryName!,
                $"{Path.GetFileNameWithoutExtension(target.Name)}_{number}{target.Extension}"));
            if (Path.Exists(candidate.FullName))
            {
                continue;
            }
            try
            {
                return source.CopyTo(candidate.FullName, overwrite: false);
            }
            catch (IOException) when (Path.Exists(candidate.FullName))
            {
                // Another process took this name between the check and the copy; try the next number.
            }
        }
    }
}