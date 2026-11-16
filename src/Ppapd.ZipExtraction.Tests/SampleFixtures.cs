using System.Security.Cryptography;
using System.Text;

namespace Ppapd.ZipExtraction.Tests;

/// <summary>The sample ZIPs in Fixtures/, made by poc/samples/make_samples.py, and what they hold.</summary>
internal static class SampleFixtures
{
    /// <summary>The password of every encrypted sample; mirrors PASSWORD in make_samples.py.</summary>
    public const string Password = "Sample-Pass-2026";

    /// <summary>The files every sample contains; mirrors PAYLOAD in make_samples.py.</summary>
    private static readonly IReadOnlyDictionary<string, string> Payload = new Dictionary<string, string>
    {
        ["図面/A-001_平面図.txt"] = "サンプルの平面図です。\n",
        ["図面/A-002_立面図.txt"] = "サンプルの立面図です。\n",
        ["仕様書.txt"] = "サンプルの仕様書です。\n",
        ["readme.txt"] = "This is a sample file for ppapd.\n",
    };

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>Each sample file's path inside the archive, mapped to the SHA-256 of its content.</summary>
    public static SortedDictionary<string, string> ExpectedFiles() => new(
        Payload.ToDictionary(entry => entry.Key, entry => Sha256(Encoding.UTF8.GetBytes(entry.Value))),
        StringComparer.Ordinal);

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static string Sha256(Stream content) => Convert.ToHexString(SHA256.HashData(content));
}
