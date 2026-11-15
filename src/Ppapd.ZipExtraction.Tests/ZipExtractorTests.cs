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

    /// <summary>The password of every encrypted sample in Fixtures/; mirrors PASSWORD in poc/samples/make_samples.py.</summary>
    private const string SamplePassword = "Sample-Pass-2026";

    private readonly DirectoryInfo destination = Directory.CreateTempSubdirectory("ppapd-test-");

    public void Dispose() => destination.Delete(recursive: true);

    [Fact]
    public void Extracting_an_unencrypted_utf8_zip_writes_every_file_under_its_folder()
    {
        using var zip = File.OpenRead(FixturePath("plain-utf8.zip"));

        ZipExtractor.Extract(zip, destination);

        Assert.Equal(ExpectedSampleFiles(), WrittenFiles());
    }

    [Fact]
    public void Extracting_a_zip_without_the_utf8_flag_reads_its_names_as_cp932()
    {
        using var zip = File.OpenRead(FixturePath("plain-cp932.zip"));

        ZipExtractor.Extract(zip, destination);

        Assert.Equal(ExpectedSampleFiles(), WrittenFiles());
    }

    [Fact]
    public void Extracting_a_zipcrypto_zip_with_its_password_writes_every_file_under_its_folder()
    {
        using var zip = File.OpenRead(FixturePath("ppap-zipcrypto-utf8.zip"));

        ZipExtractor.Extract(zip, destination, SamplePassword);

        Assert.Equal(ExpectedSampleFiles(), WrittenFiles());
    }

    [Fact]
    public void Extracting_an_aes256_zip_with_its_password_writes_every_file_under_its_folder()
    {
        using var zip = File.OpenRead(FixturePath("ppap-aes256-utf8.zip"));

        ZipExtractor.Extract(zip, destination, SamplePassword);

        Assert.Equal(ExpectedSampleFiles(), WrittenFiles());
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData(@"..\outside.txt")]
    [InlineData(@"C:\outside.txt")]
    [InlineData("/outside.txt")]
    public void Extracting_a_zip_with_an_entry_climbing_out_of_the_destination_writes_nothing(string escapingName)
    {
        var nestedDestination = destination.CreateSubdirectory("a").CreateSubdirectory("b");
        using var zip = ZipWith("readme.txt", escapingName);

        Assert.Throws<ZipSlipException>(() => ZipExtractor.Extract(zip, nestedDestination));

        Assert.Empty(WrittenFiles());
    }

    [Fact]
    public void Extracting_an_entry_under_a_part_of_two_periods_and_a_space_stays_inside_the_destination()
    {
        var nestedDestination = destination.CreateSubdirectory("a").CreateSubdirectory("b");
        using var zip = ZipWith("readme.txt", ".. /outside.txt");

        ZipExtractor.Extract(zip, nestedDestination);

        Assert.Equal(["a/b/outside.txt", "a/b/readme.txt"], WrittenFiles().Keys);
    }

    private static MemoryStream ZipWith(params string[] entryNames)
    {
        var zip = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entryNames)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write("sample\n");
            }
        }
        zip.Position = 0;
        return zip;
    }

    private static SortedDictionary<string, string> ExpectedSampleFiles() => new(
        SamplePayload.ToDictionary(entry => entry.Key, entry => Sha256(Encoding.UTF8.GetBytes(entry.Value))),
        StringComparer.Ordinal);

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private SortedDictionary<string, string> WrittenFiles() => new(
        Directory.EnumerateFiles(destination.FullName, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(destination.FullName, path).Replace('\\', '/'),
            path => Sha256(File.ReadAllBytes(path))),
        StringComparer.Ordinal);

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
