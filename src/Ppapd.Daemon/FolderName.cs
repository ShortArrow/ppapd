namespace Ppapd.Daemon;

/// <summary>Turns a name the sender of a mail can choose into a safe folder name (docs/PRD.md, §6.2).</summary>
internal static class FolderName
{
    private const char Replacement = '_';

    private static readonly char[] Forbidden = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> Reserved = new(
        ["CON", "PRN", "AUX", "NUL",
         .. Enumerable.Range(1, 9).Select(n => $"COM{n}"),
         .. Enumerable.Range(1, 9).Select(n => $"LPT{n}")],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Applies the four steps of §6.2 to <paramref name="name"/>, in their order.</summary>
    public static string Safe(string name)
    {
        var replaced = new string(name.Select(c => char.IsControl(c) || Forbidden.Contains(c) ? Replacement : c).ToArray());
        var trimmed = replaced.TrimEnd(' ', '.');
        if (trimmed is "" or "." or "..")
        {
            return Replacement.ToString();
        }
        return Reserved.Contains(trimmed.Split('.')[0].TrimEnd(' ')) ? Replacement + trimmed : trimmed;
    }
}