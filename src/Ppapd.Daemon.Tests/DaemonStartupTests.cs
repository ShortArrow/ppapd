using Ppapd.ZipExtraction;

namespace Ppapd.Daemon.Tests;

public sealed class DaemonStartupTests : IDisposable
{
    private readonly DirectoryInfo workRoot = new(Path.Combine(Path.GetTempPath(), $"ppapd-test-{Guid.NewGuid():N}"));

    public void Dispose()
    {
        if (workRoot.Exists)
        {
            workRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void Starting_removes_the_work_folders_a_daemon_that_stopped_partway_left()
    {
        var left = Directory.CreateDirectory(Path.Combine(workRoot.FullName, Guid.NewGuid().ToString("N")));
        File.WriteAllText(Path.Combine(left.FullName, WorkFolders.LockFileName), "");
        File.WriteAllText(Path.Combine(left.FullName, "half-written.txt"), "partial\n");

        DaemonStartup.Run(new WorkFolders(workRoot));

        Assert.Empty(workRoot.EnumerateFileSystemInfos());
    }

    [Fact]
    public void Starting_keeps_the_work_folder_another_daemon_is_using()
    {
        using var inUse = new WorkFolders(workRoot).Create();

        DaemonStartup.Run(new WorkFolders(workRoot));

        Assert.True(inUse.Directory.Exists);
    }
}