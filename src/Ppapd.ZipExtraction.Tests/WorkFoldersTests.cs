using System.Security.AccessControl;
using System.Security.Principal;

namespace Ppapd.ZipExtraction.Tests;

public sealed class WorkFoldersTests : IDisposable
{
    private readonly DirectoryInfo root = new(Path.Combine(Path.GetTempPath(), $"ppapd-test-{Guid.NewGuid():N}"));

    public void Dispose()
    {
        if (root.Exists)
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Each_work_folder_is_a_new_folder_of_its_own_under_the_root()
    {
        var work = new WorkFolders(root);

        using var first = work.Create();
        using var second = work.Create();

        Assert.True(first.Directory.Exists);
        Assert.True(second.Directory.Exists);
        Assert.NotEqual(first.Directory.FullName, second.Directory.FullName);
        Assert.All([first, second], folder => Assert.StartsWith(root.FullName + Path.DirectorySeparatorChar, folder.Directory.FullName));
    }

    [Fact]
    public void A_work_folder_grants_access_to_the_current_user_alone_and_inherits_nothing()
    {
        using var folder = new WorkFolders(root).Create();

        var security = folder.Directory.GetAccessControl();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToList();

        Assert.True(security.AreAccessRulesProtected);
        Assert.NotEmpty(rules);
        Assert.All(rules, rule => Assert.Equal(WindowsIdentity.GetCurrent().User, rule.IdentityReference));
    }

    [Fact]
    public void Disposing_a_work_folder_deletes_it_with_everything_in_it()
    {
        var folder = new WorkFolders(root).Create();
        File.WriteAllText(Path.Combine(folder.Directory.FullName, "extracted.txt"), "sample\n");
        folder.Directory.CreateSubdirectory("図面");

        folder.Dispose();

        Assert.Empty(root.EnumerateFileSystemInfos());
    }

    [Fact]
    public void Removing_leftovers_deletes_every_work_folder_left_by_a_process_that_stopped_partway()
    {
        LeaveFolderOfStoppedProcess("half-written.txt");
        LeaveFolderOfStoppedProcess();

        new WorkFolders(root).RemoveLeftovers();

        Assert.Empty(root.EnumerateFileSystemInfos());
    }

    [Fact]
    public void Removing_leftovers_keeps_a_work_folder_another_daemon_is_still_using()
    {
        using var inUse = new WorkFolders(root).Create();
        File.WriteAllText(Path.Combine(inUse.Directory.FullName, "extracting.txt"), "sample\n");
        LeaveFolderOfStoppedProcess();

        new WorkFolders(root).RemoveLeftovers();

        Assert.Equal([inUse.Directory.Parent!.FullName], root.EnumerateDirectories().Select(folder => folder.FullName));
        Assert.True(File.Exists(Path.Combine(inUse.Directory.FullName, "extracting.txt")));
    }

    [Fact]
    public void A_leftover_that_cannot_be_deleted_yet_is_skipped_and_the_others_are_still_removed()
    {
        LeaveFolderOfStoppedProcess("held-open.txt");
        var stuck = root.EnumerateDirectories().Single();
        LeaveFolderOfStoppedProcess("partial.txt");
        using (new FileStream(Path.Combine(stuck.FullName, "held-open.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            new WorkFolders(root).RemoveLeftovers();
        }

        Assert.Equal([stuck.FullName], root.EnumerateDirectories().Select(folder => folder.FullName));

        new WorkFolders(root).RemoveLeftovers();

        Assert.Empty(root.EnumerateFileSystemInfos());
    }
    [Fact]
    public void Removing_leftovers_before_any_work_folder_was_made_does_nothing()
    {
        new WorkFolders(root).RemoveLeftovers();

        Assert.False(root.Exists);
    }

    /// <summary>
    /// Leaves under the root what a daemon that stopped partway leaves: a work folder whose lock nobody holds any more,
    /// holding <paramref name="files"/>.
    /// </summary>
    private void LeaveFolderOfStoppedProcess(params string[] files)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root.FullName, Guid.NewGuid().ToString("N")));
        File.WriteAllText(Path.Combine(folder.FullName, WorkFolders.LockFileName), "");
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(folder.FullName, file), "partial\n");
        }
    }
}
