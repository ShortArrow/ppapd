using System.Security.Cryptography;
using System.Text;

namespace Ppapd.ZipExtraction.Tests;

public sealed class ZipExtractorTests : IDisposable
{
    /// <summary>The files every sample in Fixtures/ contains; mirrors PAYLOAD in poc/samples/make_samples.py.</summary>
    private static readonly IReadOnlyDictionary<string, string> SamplePayload = new Dictionary<string, string>
    {
        ["図面/A-001_平面図.txt"] = "サンプルの平面図です。\n",
        ["図面/A-002_立面図.txt"] = "サンプルの立面図です。\n",
        ["仕様書.txt"] = "サンプルの仕様書です。\n",
        ["readme.txt"] = "This is a sample file for ppapd.\n",
    };

    private readonly DirectoryInfo destination = Directory.CreateTempSubdirectory("ppapd-test-");

    public void Dispose() => destination.Delete(recursive: true);

    [Fact]
    public void Extracting_an_unencrypted_utf8_zip_writes_every_file_under_its_folder()
    {
        using var zip = File.OpenRead(FixturePath("plain-utf8.zip"));

        ZipExtractor.Extract(zip, destination);

        var expected = new SortedDictionary<string, string>(
            SamplePayload.ToDictionary(entry => entry.Key, entry => Sha256(Encoding.UTF8.GetBytes(entry.Value))),
            StringComparer.Ordinal);
        Assert.Equal(expected, WrittenFiles());
    }

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private SortedDictionary<string, string> WrittenFiles() => new(
        Directory.EnumerateFiles(destination.FullName, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(destination.FullName, path).Replace('\\', '/'),
            path => Sha256(File.ReadAllBytes(path))),
        StringComparer.Ordinal);

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
