using Ppapd.Daemon;
using Ppapd.ZipExtraction;

if (!CallerCheck.IsAllowed(args, new DirectoryInfo(AppContext.BaseDirectory)))
{
    return 1;
}
DaemonStartup.Run(WorkFolders.Default);
return 0;