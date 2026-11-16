using System.Text;

namespace Ppapd.ZipExtraction.Tests;

public sealed class ZipExtractorTests : IDisposable
{
    private readonly DirectoryInfo destination = Directory.CreateTempSubdirectory("ppapd-test-");

    public void Dispose() => destination.Delete(recursive: true);

    [Fact]
    public void Extracting_an_unencrypted_utf8_zip_writes_every_file_under_its_folder()
    {
        using var zip = File.OpenRead(SampleFixtures.Path("plain-utf8.zip"));

        ZipExtractor.Extract(zip, destination);

        Assert.Equal(SampleFixtures.ExpectedFiles(), WrittenFiles());
    }

    [Fact]
    public void Extracting_a_zip_without_the_utf8_flag_reads_its_names_as_cp932()
    {
        using var zip = File.OpenRead(SampleFixtures.Path("plain-cp932.zip"));

        ZipExtractor.Extract(zip, destination);

        Assert.Equal(SampleFixtures.ExpectedFiles(), WrittenFiles());
    }

    [Fact]
    public void Extracting_a_zipcrypto_zip_with_its_password_writes_every_file_under_its_folder()
    {
        using var zip = File.OpenRead(SampleFixtures.Path("ppap-zipcrypto-utf8.zip"));

        ZipExtractor.Extract(zip, destination, SampleFixtures.Password);

        Assert.Equal(SampleFixtures.ExpectedFiles(), WrittenFiles());
    }

    [Fact]
    public void Extracting_an_aes256_zip_with_its_password_writes_every_file_under_its_folder()
    {
        using var zip = File.OpenRead(SampleFixtures.Path("ppap-aes256-utf8.zip"));

        ZipExtractor.Extract(zip, destination, SampleFixtures.Password);

        Assert.Equal(SampleFixtures.ExpectedFiles(), WrittenFiles());
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

    [Fact]
    public void Extracting_a_zip_that_holds_a_zip_writes_the_inner_zip_as_it_is()
    {
        var innerZip = ZipWith("readme.txt").ToArray();
        using var zip = ZipOf(new Dictionary<string, byte[]> { ["inner.zip"] = innerZip });

        ZipExtractor.Extract(zip, destination);

        Assert.Equal(new SortedDictionary<string, string> { ["inner.zip"] = SampleFixtures.Sha256(innerZip) }, WrittenFiles());
    }

    private static MemoryStream ZipWith(params string[] entryNames) =>
        ZipOf(entryNames.ToDictionary(name => name, _ => Encoding.UTF8.GetBytes("sample\n")));

    private static MemoryStream ZipOf(IReadOnlyDictionary<string, byte[]> entries)
    {
        var zip = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(content);
            }
        }
        zip.Position = 0;
        return zip;
    }

    private SortedDictionary<string, string> WrittenFiles() => new(
        Directory.EnumerateFiles(destination.FullName, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(destination.FullName, path).Replace('\\', '/'),
            path => SampleFixtures.Sha256(File.ReadAllBytes(path))),
        StringComparer.Ordinal);
}
