using System.Diagnostics;
using Ppapd.Daemon;
using Ppapd.ZipExtraction;

if (!CallerCheck.IsAllowed(args, new DirectoryInfo(AppContext.BaseDirectory)))
{
    return 1;
}
DaemonStartup.Run(WorkFolders.Default);

var home = Directory.CreateDirectory(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ppapd"));
var logPath = Path.Combine(home.FullName, "ppapd.log");
var rulesPath = Path.Combine(home.FullName, "rules.yaml");

using var ledger = new DeliveryLedger(Path.Combine(home.FullName, "ppapd.db"));
var handler = new MessageHandler(LoadSettings, ledger, WorkFolders.Default, Reveal, Log);
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();
MessageLoop.Run(input, output, handler.Handle, Log);
return 0;

Settings LoadSettings()
{
    if (!File.Exists(rulesPath))
    {
        throw new InvalidRulesException($"{rulesPath} does not exist.");
    }
    using var reader = File.OpenText(rulesPath);
    return RulesFile.Load(reader);
}

void Log(string line) => File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");

static void Reveal(DirectoryInfo folder) => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder.FullName } })?.Dispose();