using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Ppapd.ZipExtraction.Tests;

public sealed class ZipRepackerTests
{
    [Theory]
    [InlineData("ppap-zipcrypto-utf8.zip")]
    [InlineData("ppap-zipcrypto-cp932.zip")]
    [InlineData("ppap-aes256-utf8.zip")]
    [InlineData("ppap-aes256-cp932.zip")]
    public void Repacking_an_encrypted_zip_writes_its_files_without_a_password_under_utf8_names_with_its_sha256_as_comment(string sample)
    {
        var encrypted = File.ReadAllBytes(SampleFixtures.Path(sample));
        using var output = new MemoryStream();

        ZipRepacker.Repack(new MemoryStream(encrypted), SampleFixtures.Password, output);

        output.Position = 0;
        using var repacked = new ZipArchive(output, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Cp932);
        Assert.Equal(SampleFixtures.ExpectedFiles(), FilesIn(repacked));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(encrypted)), repacked.Comment);
    }

    /// <summary>
    /// Read with CP932 as the fallback, so names come out right only when the UTF-8 flag is set;
    /// reading the content fails if an entry is still encrypted.
    /// </summary>
    private static Encoding Cp932 => CodePagesEncodingProvider.Instance.GetEncoding(932)!;

    private static SortedDictionary<string, string> FilesIn(ZipArchive archive) => new(
        archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var content = entry.Open();
                return SampleFixtures.Sha256(content);
            }),
        StringComparer.Ordinal);
}
