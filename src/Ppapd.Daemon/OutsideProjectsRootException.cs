namespace Ppapd.Daemon;

/// <summary>
/// A delivery folder, after its names were made safe, still does not resolve under the projects root; nothing is
/// delivered and this is recorded as an error (docs/PRD.md, §6.2).
/// </summary>
public sealed class OutsideProjectsRootException() : Exception("The delivery folder does not resolve under projects_root.")
{
}