namespace Ppapd.Daemon;

/// <summary>A path sent with <c>download</c> does not name a file under the download folder (docs/PRD.md, F-5.1).</summary>
public sealed class OutsideDownloadFolderException : Exception
{
}