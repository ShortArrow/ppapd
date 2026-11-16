using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Ppapd.ZipExtraction.Tests;

public sealed class ZipDeliveryTests : IDisposable
{
    private readonly DirectoryInfo downloads = Directory.CreateTempSubdirectory("ppapd-test-");
    private readonly DirectoryInfo destination = Directory.CreateTempSubdirectory("ppapd-test-");

    public void Dispose()
    {
        downloads.Delete(recursive: true);
        destination.Delete(recursive: true);
    }

    [Fact]
    public void Delivering_an_encrypted_zip_with_its_password_writes_its_files_and_a_repacked_zip_of_the_same_name()
    {
        var zip = new FileInfo(SampleFixtures.Path("ppap-zipcrypto-utf8.zip"));

        var blocked = ZipDelivery.Deliver(zip, destination, SampleFixtures.Password);

        Assert.Null(blocked);
        var written = WrittenFiles();
        Assert.True(written.Remove(zip.Name));
        Assert.Equal(SampleFixtures.ExpectedFiles(), written);
        using var repacked = ZipFile.OpenRead(Path.Combine(destination.FullName, zip.Name));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip.FullName))), repacked.Comment);
    }

    [Fact]
    public void Delivering_an_unencrypted_zip_writes_its_files_and_no_repacked_zip()
    {
        var zip = new FileInfo(SampleFixtures.Path("plain-utf8.zip"));

        var blocked = ZipDelivery.Deliver(zip, destination, password: null);

        Assert.Null(blocked);
        Assert.Equal(SampleFixtures.ExpectedFiles(), WrittenFiles());
    }

    [Fact]
    public void A_zip_with_an_entry_climbing_out_is_not_repacked_and_leaves_only_its_zip_slip_notice()
    {
        var zip = SaveZipWith("見積書.zip", "readme.txt", "../outside.txt");

        var blocked = ZipDelivery.Deliver(zip, destination, SampleFixtures.Password);

        Assert.Equal(BlockReason.ZipSlip, blocked);
        Assert.Equal(["見積書.zip.blocked.txt"], WrittenFiles().Keys);
        Assert.StartsWith("「見積書.zip」は安全のため", File.ReadAllText(Path.Combine(destination.FullName, "見積書.zip.blocked.txt")));
    }

    [Fact]
    public void A_zip_past_the_extraction_limits_is_not_repacked_and_leaves_only_its_size_notice()
    {
        var zip = SaveZipWith("図面一式.zip", Enumerable.Range(0, 10_001).Select(index => $"{index}.txt").ToArray());

        var blocked = ZipDelivery.Deliver(zip, destination, SampleFixtures.Password);

        Assert.Equal(BlockReason.TooLarge, blocked);
        Assert.Equal(["図面一式.zip.blocked.txt"], WrittenFiles().Keys);
        Assert.StartsWith("「図面一式.zip」は配送しませんでした。", File.ReadAllText(Path.Combine(destination.FullName, "図面一式.zip.blocked.txt")));
    }

    /// <summary>Saves into the download folder an unencrypted ZIP holding each of <paramref name="entryNames"/>.</summary>
    private FileInfo SaveZipWith(string zipName, params string[] entryNames)
    {
        var zip = new FileInfo(Path.Combine(downloads.FullName, zipName));
        using (var archive = ZipFile.Open(zip.FullName, ZipArchiveMode.Create))
        {
            foreach (var name in entryNames)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes("sample\n"));
            }
        }
        return zip;
    }

    private SortedDictionary<string, string> WrittenFiles() => new(
        Directory.EnumerateFiles(destination.FullName, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(destination.FullName, path).Replace('\\', '/'),
            path => SampleFixtures.Sha256(File.ReadAllBytes(path))),
        StringComparer.Ordinal);
}
