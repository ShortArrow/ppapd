using System.Security.AccessControl;
using System.Security.Principal;

namespace Ppapd.ZipExtraction;

/// <summary>
/// The folders on the user's PC where a ZIP is extracted and repacked before it is delivered (docs/PRD.md, F-4.8;
/// docs/ARCHITECTURE.md, where data is kept).
/// </summary>
/// <remarks>
/// Several daemons can run at once, one for each browser profile, all sharing the root. A work folder in use holds its
/// lock file open, so only those left by a daemon that stopped partway have a lock nobody holds.
/// </remarks>
/// <param name="root">The folder that holds every work folder.</param>
public sealed class WorkFolders(DirectoryInfo root)
{
    /// <summary>The name of the file each work folder holds open while in use.</summary>
    public const string LockFileName = ".lock";

    /// <summary>The work folders under <c>%LOCALAPPDATA%\ppapd\work</c>.</summary>
    public static WorkFolders Default { get; } = new(new DirectoryInfo(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ppapd", "work")));

    /// <summary>
    /// Creates a new empty folder under the root, unlike any made before, that only the current user can open
    /// and that inherits no permission from the root. It stays locked until disposed.
    /// </summary>
    public WorkFolder Create()
    {
        root.Create();
        var folder = new DirectoryInfo(Path.Combine(root.FullName, Guid.NewGuid().ToString("N")));
        folder.Create(CurrentUserOnly());
        var held = Lock(folder, FileMode.CreateNew);
        var work = new WorkFolder(folder, held);
        work.Directory.Create(CurrentUserOnly());
        work.Repacked.Create(CurrentUserOnly());
        return work;
    }

    /// <summary>
    /// Deletes every work folder under the root whose lock nobody holds, that is, those left when a daemon stopped
    /// partway. Folders other daemons are using are kept, and one that cannot be deleted yet is skipped; this never throws
    /// for a single folder, so the daemon still starts.
    /// </summary>
    public void RemoveLeftovers()
    {
        if (!root.Exists)
        {
            return;
        }
        foreach (var folder in root.EnumerateDirectories())
        {
            FileStream held;
            try
            {
                held = Lock(folder, FileMode.OpenOrCreate);
            }
            catch (IOException)
            {
                continue;
            }
            try
            {
                new WorkFolder(folder, held).Dispose();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Something else still holds a file in it; the next start tries again.
            }
        }
    }

    /// <summary>Opens the lock file of <paramref name="folder"/> for this process alone; closing it deletes the file.</summary>
    private static FileStream Lock(DirectoryInfo folder, FileMode mode) =>
        new(Path.Combine(folder.FullName, LockFileName), mode, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);

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

/// <summary>One work folder of <see cref="WorkFolders"/>, locked while this object lives.</summary>
/// <remarks>
/// The lock file sits beside <see cref="Directory"/> and <see cref="Repacked"/>, not in them, so that nothing extracted can collide with it
/// and nothing copied out of them includes it.
/// </remarks>
public sealed class WorkFolder : IDisposable
{
    private readonly DirectoryInfo folder;
    private readonly FileStream held;

    internal WorkFolder(DirectoryInfo folder, FileStream held)
    {
        this.folder = folder;
        this.held = held;
        Directory = new DirectoryInfo(Path.Combine(folder.FullName, "files"));
        Repacked = new DirectoryInfo(Path.Combine(folder.FullName, "repacked"));
    }

    /// <summary>The empty folder to extract into.</summary>
    public DirectoryInfo Directory { get; }

    /// <summary>
    /// The empty folder to write the repacked ZIP into, apart from <see cref="Directory"/> so that an extracted file
    /// named like the ZIP does not collide with it.
    /// </summary>
    public DirectoryInfo Repacked { get; }

    /// <summary>Deletes everything in the work folder while still holding its lock, then releases the lock and removes the folder.</summary>
    /// <exception cref="IOException">Something still holds a file in the folder; the lock is released all the same.</exception>
    public void Dispose()
    {
        try
        {
            foreach (var info in folder.EnumerateFileSystemInfos().Where(info => info.Name != WorkFolders.LockFileName))
            {
                if (info is DirectoryInfo directory)
                {
                    directory.Delete(recursive: true);
                }
                else
                {
                    info.Delete();
                }
            }
        }
        finally
        {
            held.Dispose();
        }
        try
        {
            folder.Delete();
        }
        catch (IOException)
        {
            // Another daemon locked the emptied folder as a leftover between the release and this delete; it removes it.
        }
    }
}
