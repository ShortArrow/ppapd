using System.Diagnostics;

namespace Ppapd.Daemon.Tests;

public sealed class DownloadPathTests : IDisposable
{
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("ppapd-test-");
    private readonly DirectoryInfo downloads;
    private readonly DirectoryInfo outside;

    public DownloadPathTests()
    {
        downloads = temp.CreateSubdirectory("ppapd");
        outside = temp.CreateSubdirectory("outside");
        File.WriteAllText(Path.Combine(downloads.FullName, "a.zip"), "sample\n");
        File.WriteAllText(Path.Combine(downloads.CreateSubdirectory("sub").FullName, "a.zip"), "sample\n");
        File.WriteAllText(Path.Combine(outside.FullName, "a.zip"), "sample\n");
        File.WriteAllText(Path.Combine(temp.CreateSubdirectory("ppapd-other").FullName, "a.zip"), "sample\n");
    }

    /// <summary>Removes the junctions on their own first, because a recursive delete refuses them.</summary>
    public void Dispose()
    {
        foreach (var link in downloads.EnumerateDirectories().Where(folder => folder.Attributes.HasFlag(FileAttributes.ReparsePoint)))
        {
            link.Delete();
        }
        temp.Delete(recursive: true);
    }

    [Theory]
    [InlineData(@"ppapd\a.zip")]
    [InlineData(@"ppapd\sub\a.zip")]
    public void A_file_under_the_download_folder_is_accepted(string underTemp)
    {
        var path = Path.Combine(temp.FullName, underTemp);

        Assert.Equal(path, DownloadPath.Accept(path, downloads).FullName);
    }

    [Theory]
    [InlineData(@"ppapd\..\outside\a.zip")]
    [InlineData(@"outside\a.zip")]
    [InlineData(@"ppapd-other\a.zip")]
    [InlineData(@"ppapd\a.zip:hidden")]
    [InlineData(@"ppapd\missing.zip")]
    [InlineData(@"ppapd\sub")]
    [InlineData(@"ppapd")]
    public void A_path_that_is_not_a_file_under_the_download_folder_is_refused(string underTemp)
    {
        var path = Path.Combine(temp.FullName, underTemp);

        Assert.Throws<OutsideDownloadFolderException>(() => DownloadPath.Accept(path, downloads));
    }

    [Fact]
    public void A_relative_path_is_refused()
    {
        Assert.Throws<OutsideDownloadFolderException>(() => DownloadPath.Accept("a.zip", downloads));
    }

    [Fact]
    public void A_path_through_a_junction_under_the_download_folder_to_a_folder_outside_is_refused()
    {
        var link = Path.Combine(downloads.FullName, "link");
        MakeJunction(link, outside.FullName);

        Assert.Throws<OutsideDownloadFolderException>(() => DownloadPath.Accept(Path.Combine(link, "a.zip"), downloads));
    }

    private static void MakeJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd", ["/c", "mklink", "/J", link, target])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
