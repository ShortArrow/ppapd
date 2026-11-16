using System.Security.AccessControl;
using System.Security.Principal;

namespace Ppapd.ZipExtraction;

/// <summary>
/// The folders on the user's PC where a ZIP is extracted and repacked before it is delivered (docs/PRD.md, F-4.8;
/// docs/ARCHITECTURE.md, where data is kept).
/// </summary>
/// <param name="root">The folder that holds every work folder.</param>
public sealed class WorkFolders(DirectoryInfo root)
{
    /// <summary>The work folders under <c>%LOCALAPPDATA%\ppapd\work</c>.</summary>
    public static WorkFolders Default { get; } = new(new DirectoryInfo(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ppapd", "work")));

    /// <summary>
    /// Creates a new empty folder under the root, unlike any made before, that only the current user can open
    /// and that inherits no permission from the root.
    /// </summary>
    public DirectoryInfo Create()
    {
        root.Create();
        var folder = new DirectoryInfo(Path.Combine(root.FullName, Guid.NewGuid().ToString("N")));
        folder.Create(CurrentUserOnly());
        return folder;
    }

    /// <summary>Deletes every work folder under the root, such as those left when the daemon stopped partway.</summary>
    public void RemoveLeftovers()
    {
        if (!root.Exists)
        {
            return;
        }
        foreach (var folder in root.EnumerateDirectories())
        {
            folder.Delete(recursive: true);
        }
    }

    private static DirectorySecurity CurrentUserOnly()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }
}
