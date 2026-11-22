using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Ppapd.ZipExtraction.Tests;

public sealed class ZipDeliveryTests : IDisposable
{
    private readonly DirectoryInfo downloads = Directory.CreateTempSubdirectory("ppapd-test-");
    private readonly DirectoryInfo destination = Directory.CreateTempSubdirectory("ppapd-test-");
    private readonly DirectoryInfo workRoot = Directory.CreateTempSubdirectory("ppapd-test-");

    private WorkFolders Work => new(workRoot);

    public void Dispose()
    {
        downloads.Delete(recursive: true);
        destination.Delete(recursive: true);
        workRoot.Delete(recursive: true);
    }

    [Fact]
    public void Delivering_an_encrypted_zip_with_its_password_writes_its_files_and_a_repacked_zip_of_the_same_name()
    {
        var zip = new FileInfo(SampleFixtures.Path("ppap-zipcrypto-utf8.zip"));

        var blocked = ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work).Blocked;

        Assert.Null(blocked);
        var written = WrittenFiles();
        Assert.True(written.Remove(zip.Name));
        Assert.Equal(SampleFixtures.ExpectedFiles(), written);
        using var repacked = ZipFile.OpenRead(Path.Combine(destination.FullName, zip.Name));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip.FullName))), repacked.Comment);
        Assert.Empty(workRoot.EnumerateFileSystemInfos());
    }

    [Fact]
    public void Delivered_files_take_the_permissions_of_the_destination_and_not_those_of_the_work_folder()
    {
        var zip = new FileInfo(SampleFixtures.Path("ppap-zipcrypto-utf8.zip"));

        ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work);

        Assert.All(destination.EnumerateFiles("*", SearchOption.AllDirectories), file =>
        {
            var security = file.GetAccessControl();
            Assert.False(security.AreAccessRulesProtected);
            Assert.NotEmpty(security.GetAccessRules(includeExplicit: false, includeInherited: true, typeof(System.Security.Principal.SecurityIdentifier)));
        });
    }

    [Fact]
    public void Delivering_an_unencrypted_zip_writes_its_files_and_no_repacked_zip()
    {
        var zip = new FileInfo(SampleFixtures.Path("plain-utf8.zip"));

        var blocked = ZipDelivery.Deliver(zip, destination, password: null, Work).Blocked;

        Assert.Null(blocked);
        Assert.Equal(SampleFixtures.ExpectedFiles(), WrittenFiles());
        Assert.Empty(workRoot.EnumerateFileSystemInfos());
    }

    [Fact]
    public void A_zip_with_an_entry_climbing_out_is_not_repacked_and_leaves_only_its_zip_slip_notice()
    {
        var zip = SaveZipWith("見積書.zip", "readme.txt", "../outside.txt");

        var blocked = ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work).Blocked;

        Assert.Equal(BlockReason.ZipSlip, blocked);
        Assert.Equal(["見積書.zip.blocked.txt"], WrittenFiles().Keys);
        Assert.StartsWith("「見積書.zip」は安全のため", File.ReadAllText(Path.Combine(destination.FullName, "見積書.zip.blocked.txt")));
        Assert.Empty(workRoot.EnumerateFileSystemInfos());
    }

    [Fact]
    public void A_zip_past_the_extraction_limits_is_not_repacked_and_leaves_only_its_size_notice()
    {
        var zip = SaveZipWith("図面一式.zip", Enumerable.Range(0, 10_001).Select(index => $"{index}.txt").ToArray());

        var blocked = ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work).Blocked;

        Assert.Equal(BlockReason.TooLarge, blocked);
        Assert.Equal(["図面一式.zip.blocked.txt"], WrittenFiles().Keys);
        Assert.StartsWith("「図面一式.zip」は配送しませんでした。", File.ReadAllText(Path.Combine(destination.FullName, "図面一式.zip.blocked.txt")));
        Assert.Empty(workRoot.EnumerateFileSystemInfos());
    }

    [Fact]
    public void An_entry_named_like_the_zip_and_the_repacked_zip_are_both_delivered_and_the_comment_tells_the_repacked_one()
    {
        var zip = SaveZipWith("見積書.zip", "readme.txt", "見積書.zip");

        ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work);

        Assert.Equal(["readme.txt", "見積書.zip", "見積書_1.zip"], WrittenFiles().Keys);
        Assert.Equal(SampleFixtures.Sha256("sample\n"u8.ToArray()), WrittenFiles()["見積書.zip"]);
        using var repacked = ZipFile.OpenRead(Path.Combine(destination.FullName, "見積書_1.zip"));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip.FullName))), repacked.Comment);
        Assert.Empty(workRoot.EnumerateFileSystemInfos());
    }

    [Fact]
    public void A_file_already_in_the_destination_is_kept_and_the_delivered_one_gets_the_next_number()
    {
        File.WriteAllText(Path.Combine(destination.FullName, "仕様書.txt"), "kept\n");
        File.WriteAllText(Path.Combine(destination.FullName, "仕様書_1.txt"), "kept too\n");
        var zip = new FileInfo(SampleFixtures.Path("plain-utf8.zip"));

        ZipDelivery.Deliver(zip, destination, password: null, Work);

        Assert.Equal("kept\n", File.ReadAllText(Path.Combine(destination.FullName, "仕様書.txt")));
        Assert.Equal("kept too\n", File.ReadAllText(Path.Combine(destination.FullName, "仕様書_1.txt")));
        Assert.Equal(SampleFixtures.ExpectedFiles()["仕様書.txt"], WrittenFiles()["仕様書_2.txt"]);
    }

    [Fact]
    public void Delivering_the_same_encrypted_zip_twice_adds_nothing_the_second_time()
    {
        var zip = new FileInfo(SampleFixtures.Path("ppap-zipcrypto-utf8.zip"));
        ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work);
        var first = WrittenFiles();

        ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work);

        Assert.Equal(first, WrittenFiles());
    }

    [Fact]
    public void A_look_alike_zip_with_the_right_comment_but_other_contents_does_not_stand_in_for_the_repacked_one()
    {
        var zip = new FileInfo(SampleFixtures.Path("ppap-zipcrypto-utf8.zip"));
        var lookAlike = Path.Combine(destination.FullName, zip.Name);
        using (var archive = ZipFile.Open(lookAlike, ZipArchiveMode.Create))
        {
            archive.Comment = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip.FullName)));
            using var stream = archive.CreateEntry("readme.txt").Open();
            stream.Write("not what was sent\n"u8);
        }

        ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work);

        var repacked = Path.Combine(destination.FullName, Path.GetFileNameWithoutExtension(zip.Name) + "_1.zip");
        using var real = ZipFile.OpenRead(repacked);
        Assert.Equal(SampleFixtures.ExpectedFiles().Keys, real.Entries.Where(entry => !entry.FullName.EndsWith('/')).Select(entry => entry.FullName).Order(StringComparer.Ordinal));
    }
    [Fact]
    public void A_look_alike_zip_whose_entry_holds_more_than_its_header_says_is_read_no_further_than_the_repacked_one()
    {
        var zip = new FileInfo(SampleFixtures.Path("ppap-zipcrypto-utf8.zip"));
        var repackedSize = (int)RepackedEntryLength(zip, "readme.txt");
        var lookAlike = Path.Combine(destination.FullName, zip.Name);
        using (var archive = ZipFile.Open(lookAlike, ZipArchiveMode.Create))
        {
            archive.Comment = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip.FullName)));
            foreach (var name in SampleFixtures.ExpectedFiles().Keys)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(name == "readme.txt" ? new byte[64 * 1024 * 1024] : Encoding.UTF8.GetBytes("x"));
            }
        }
        var started = System.Diagnostics.Stopwatch.StartNew();

        ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work);

        Assert.True(File.Exists(Path.Combine(destination.FullName, Path.GetFileNameWithoutExtension(zip.Name) + "_1.zip")));
        Assert.True(repackedSize < 1024);
    }
    [Fact]
    public void A_file_whose_content_matches_a_numbered_one_already_there_is_not_delivered_again()
    {
        File.WriteAllText(Path.Combine(destination.FullName, "仕様書.txt"), "different\n");
        File.WriteAllText(Path.Combine(destination.FullName, "仕様書_1.txt"), "サンプルの仕様書です。\n");
        var zip = new FileInfo(SampleFixtures.Path("plain-utf8.zip"));

        ZipDelivery.Deliver(zip, destination, password: null, Work);

        Assert.DoesNotContain("仕様書_2.txt", WrittenFiles().Keys);
        Assert.Equal("different\n", File.ReadAllText(Path.Combine(destination.FullName, "仕様書.txt")));
    }

    [Fact]
    public void Only_the_files_whose_content_differs_from_those_already_there_are_delivered_with_a_number()
    {
        File.WriteAllText(Path.Combine(destination.FullName, "仕様書.txt"), "サンプルの仕様書です。\n");
        File.WriteAllText(Path.Combine(destination.FullName, "readme.txt"), "different\n");
        var zip = new FileInfo(SampleFixtures.Path("plain-utf8.zip"));

        ZipDelivery.Deliver(zip, destination, password: null, Work);

        Assert.Equal(
            ["readme.txt", "readme_1.txt", "仕様書.txt", "図面/A-001_平面図.txt", "図面/A-002_立面図.txt"],
            WrittenFiles().Keys);
    }

    [Fact]
    public void Copying_into_the_destination_failing_partway_removes_what_was_copied_and_keeps_what_was_there()
    {
        File.WriteAllText(Path.Combine(destination.FullName, "図面"), "a file where the zip has a folder\n");
        var zip = new FileInfo(SampleFixtures.Path("plain-utf8.zip"));

        Assert.ThrowsAny<IOException>(() => ZipDelivery.Deliver(zip, destination, password: null, Work));

        Assert.Equal(["図面"], WrittenFiles().Keys);
        Assert.Empty(destination.EnumerateDirectories());
        Assert.Empty(workRoot.EnumerateFileSystemInfos());
    }

    [Fact]
    public void Delivering_lists_the_files_it_wrote_relative_to_the_destination_numbered_as_written()
    {
        File.WriteAllText(Path.Combine(destination.FullName, "readme.txt"), "different\n");
        var zip = new FileInfo(SampleFixtures.Path("ppap-zipcrypto-utf8.zip"));

        var result = ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work);

        Assert.Equal(
            new[] { "readme_1.txt", "仕様書.txt", "図面/A-001_平面図.txt", "図面/A-002_立面図.txt", zip.Name }.Order(StringComparer.Ordinal),
            result.Written.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Files_already_there_with_the_same_content_are_listed_as_found_not_as_written()
    {
        var zip = new FileInfo(SampleFixtures.Path("plain-utf8.zip"));
        ZipDelivery.Deliver(zip, destination, password: null, Work);

        var result = ZipDelivery.Deliver(zip, destination, password: null, Work);

        Assert.Empty(result.Written);
        Assert.Equal(SampleFixtures.ExpectedFiles().Keys, result.Found.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_blocked_zip_lists_only_its_notice_as_written()
    {
        var zip = SaveZipWith("見積書.zip", "readme.txt", "../outside.txt");

        var result = ZipDelivery.Deliver(zip, destination, password: null, Work);

        Assert.Equal(["見積書.zip.blocked.txt"], result.Written);
    }

    private static long RepackedEntryLength(FileInfo zip, string name)
    {
        using var source = zip.OpenRead();
        using var output = new MemoryStream();
        ZipRepacker.Repack(source, SampleFixtures.Password, output);
        output.Position = 0;
        using var archive = new ZipArchive(output);
        return archive.GetEntry(name)!.Length;
    }

    private static readonly byte[] InternetMark = Encoding.ASCII.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://outlook.office.com/\r\n");

    [Fact]
    public void Files_extracted_from_a_marked_zip_and_its_repacked_zip_carry_the_same_internet_mark()
    {
        var zip = new FileInfo(Path.Combine(downloads.FullName, "ppap-zipcrypto-utf8.zip"));
        File.Copy(SampleFixtures.Path(zip.Name), zip.FullName);
        File.WriteAllBytes(zip.FullName + ":Zone.Identifier", InternetMark);

        var written = ZipDelivery.Deliver(zip, destination, SampleFixtures.Password, Work).Written;

        Assert.Contains(zip.Name, written);
        Assert.All(written, name => Assert.Equal(InternetMark, MarkOf(name)));
    }

    [Fact]
    public void A_mark_written_by_an_entry_gives_way_to_the_mark_of_the_zip()
    {
        var zip = SaveZipWith("見積書.zip", "doc.docm", "doc.docm:Zone.Identifier");
        File.WriteAllBytes(zip.FullName + ":Zone.Identifier", InternetMark);

        ZipDelivery.Deliver(zip, destination, password: null, Work);

        Assert.Equal(InternetMark, MarkOf("doc.docm"));
    }

    [Fact]
    public void Files_from_a_zip_without_a_mark_carry_none_even_when_an_entry_wrote_one()
    {
        var zip = SaveZipWith("見積書.zip", "readme.txt", "doc.docm", "doc.docm:Zone.Identifier");

        var written = ZipDelivery.Deliver(zip, destination, password: null, Work).Written;

        Assert.All(written, name => Assert.Null(MarkOf(name)));
    }

    [Fact]
    public void Marking_a_file_that_cannot_take_the_mark_leaves_it_as_it_was_and_says_so()
    {
        var file = new FileInfo(Path.Combine(destination.FullName, "readonly.txt"));
        File.WriteAllText(file.FullName, "sample\n");
        file.IsReadOnly = true;
        try
        {
            Assert.False(ZoneMark.Apply(file, InternetMark));
            Assert.Null(MarkOf("readonly.txt"));
        }
        finally
        {
            file.IsReadOnly = false;
        }
    }

    [Fact]
    public void Files_that_cannot_take_the_mark_are_still_delivered_and_reported_unmarked()
    {
        var zip = SaveZipWith("見積書.zip", "readme.txt");
        File.WriteAllBytes(zip.FullName + ":Zone.Identifier", InternetMark);

        var result = ZipDelivery.Deliver(zip, destination, password: null, Work, applyMark: (_, _) => false);

        Assert.Null(result.Blocked);
        Assert.Equal(["readme.txt"], result.Written);
        Assert.Equal(["readme.txt"], result.Unmarked);
        Assert.Equal(["readme.txt"], WrittenFiles().Keys);
    }

    private byte[]? MarkOf(string name)
    {
        var stream = Path.Combine(destination.FullName, name.Replace('/', Path.DirectorySeparatorChar)) + ":Zone.Identifier";
        return File.Exists(stream) ? File.ReadAllBytes(stream) : null;
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
