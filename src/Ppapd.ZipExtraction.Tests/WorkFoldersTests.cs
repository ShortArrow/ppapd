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

        var first = work.Create();
        var second = work.Create();

        Assert.True(first.Exists);
        Assert.True(second.Exists);
        Assert.NotEqual(first.FullName, second.FullName);
        Assert.All([first, second], folder => Assert.Equal(root.FullName, folder.Parent!.FullName));
    }

    [Fact]
    public void A_work_folder_grants_access_to_the_current_user_alone_and_inherits_nothing()
    {
        var folder = new WorkFolders(root).Create();

        var security = folder.GetAccessControl();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToList();

        Assert.True(security.AreAccessRulesProtected);
        Assert.NotEmpty(rules);
        Assert.All(rules, rule => Assert.Equal(WindowsIdentity.GetCurrent().User, rule.IdentityReference));
    }

    [Fact]
    public void Removing_leftovers_deletes_every_work_folder_left_under_the_root()
    {
        var work = new WorkFolders(root);
        var left = work.Create();
        File.WriteAllText(Path.Combine(left.FullName, "half-written.txt"), "partial\n");
        work.Create().CreateSubdirectory("図面");

        work.RemoveLeftovers();

        Assert.Empty(root.EnumerateFileSystemInfos());
    }

    [Fact]
    public void Removing_leftovers_before_any_work_folder_was_made_does_nothing()
    {
        new WorkFolders(root).RemoveLeftovers();

        Assert.False(root.Exists);
    }
}
