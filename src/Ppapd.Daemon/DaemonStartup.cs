using Ppapd.ZipExtraction;

namespace Ppapd.Daemon;

/// <summary>What the daemon does once each time the browser starts it, before reading any message.</summary>
internal static class DaemonStartup
{
    /// <summary>Removes the work folders left by daemons that stopped partway (docs/ARCHITECTURE.md, where data is kept).</summary>
    public static void Run(WorkFolders work) => work.RemoveLeftovers();
}