namespace Ppapd.ZipExtraction;

/// <summary>Copies a file without overwriting, numbering its name when taken (docs/PRD.md, F-6.3).</summary>
public static class NumberedCopy
{
    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="target"/>, or, when that name is taken, to the first free of
    /// <c>name_1.ext</c>, <c>name_2.ext</c>, and so on in the same folder. When one of the files met on the way is the same
    /// as <paramref name="source"/> by <paramref name="isSame"/>, copies nothing.
    /// </summary>
    /// <param name="isSame">Whether a file already in the folder holds what <paramref name="source"/> holds.</param>
    /// <returns>The file written, or the one found the same, told apart by <see cref="CopyOutcome.Copied"/>.</returns>
    public static CopyOutcome To(FileInfo source, FileInfo target, Func<FileInfo, bool> isSame)
    {
        for (var number = 0; ; number++)
        {
            var candidate = number == 0 ? target : new FileInfo(Path.Combine(
                target.DirectoryName!,
                $"{Path.GetFileNameWithoutExtension(target.Name)}_{number}{target.Extension}"));
            if (Path.Exists(candidate.FullName))
            {
                if (candidate.Exists && isSame(candidate))
                {
                    return new CopyOutcome(candidate, Copied: false);
                }
                continue;
            }
            try
            {
                return new CopyOutcome(source.CopyTo(candidate.FullName, overwrite: false), Copied: true);
            }
            catch (IOException) when (Path.Exists(candidate.FullName))
            {
                // Another process took this name between the check and the copy; look at it as at any other taken name.
                number--;
            }
        }
    }

    /// <summary>Whether <paramref name="file"/> and <paramref name="other"/> hold the same bytes, by their SHA-256 (F-6.3).</summary>
    public static bool SameContent(FileInfo file, FileInfo other) =>
        file.Length == other.Length && Sha256Of(file).SequenceEqual(Sha256Of(other));

    /// <summary>The SHA-256 of what <paramref name="file"/> holds.</summary>
    public static byte[] Sha256Of(FileInfo file)
    {
        using var stream = file.OpenRead();
        return System.Security.Cryptography.SHA256.HashData(stream);
    }
}
/// <summary>Where <see cref="NumberedCopy.To"/> left the file: copied there, or found already there with the same content.</summary>
public sealed record CopyOutcome(FileInfo File, bool Copied);
