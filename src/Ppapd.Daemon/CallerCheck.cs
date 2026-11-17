using System.Text.Json;

namespace Ppapd.Daemon;

/// <summary>The check that the browser started the daemon for ppapd's extension (docs/PRD.md, S-2 and S-3).</summary>
internal static class CallerCheck
{
    /// <summary>The host manifest of S-2, beside the daemon's executable.</summary>
    public const string ManifestFileName = "host-manifest.json";

    /// <summary>
    /// Whether the first of <paramref name="args"/>, the origin Chrome passes, is one of the <c>allowed_origins</c> of the
    /// manifest in <paramref name="hostFolder"/>. A manifest that is missing or cannot be read allows no one.
    /// </summary>
    public static bool IsAllowed(IReadOnlyList<string> args, DirectoryInfo hostFolder) =>
        args.Count > 0 && AllowedOrigins(hostFolder).Contains(args[0], StringComparer.Ordinal);

    private static IReadOnlyList<string> AllowedOrigins(DirectoryInfo hostFolder)
    {
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(hostFolder.FullName, ManifestFileName)));
            return manifest.RootElement.GetProperty("allowed_origins").EnumerateArray().Select(origin => origin.GetString()!).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
            or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }
}