using System.Text.RegularExpressions;

namespace Ppapd.Daemon;

/// <summary>Decides where a mail's files are delivered (docs/PRD.md, §3).</summary>
internal static partial class DeliveryFolder
{
    /// <summary>
    /// <c>&lt;projects root&gt;\&lt;project&gt;\&lt;sender&gt;\&lt;received yyyyMMdd&gt;</c> for <paramref name="mail"/>,
    /// with the project and sender names made safe as §6.2 sets.
    /// </summary>
    /// <exception cref="OutsideProjectsRootException">The folder does not resolve under the projects root.</exception>
    public static DirectoryInfo For(DeliveryRules rules, MailMetadata mail)
    {
        var domain = DomainOf(mail.SenderAddress);
        var path = Path.Combine(
            rules.ProjectsRoot,
            FolderName.Safe(ProjectOf(rules, domain, mail.Subject)),
            FolderName.Safe(SenderOf(rules, mail.SenderAddress, domain)),
            mail.Received.ToLocalTime().ToString("yyyyMMdd"));
        return EnsureUnderRoot(path, rules.ProjectsRoot);
    }

    /// <summary>The folder <paramref name="path"/> resolves to, when it lies under <paramref name="root"/>.</summary>
    /// <exception cref="OutsideProjectsRootException">It does not.</exception>
    public static DirectoryInfo EnsureUnderRoot(string path, string root)
    {
        var under = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(under, StringComparison.OrdinalIgnoreCase))
        {
            throw new OutsideProjectsRootException();
        }
        return new DirectoryInfo(full);
    }

    private static string DomainOf(string address) => address[(address.LastIndexOf('@') + 1)..];

    private static string ProjectOf(DeliveryRules rules, string domain, string subject)
    {
        foreach (var rule in rules.Projects)
        {
            if (rule.FromDomain is not null && !rule.FromDomain.Equals(domain, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var match = rule.SubjectRegex is null ? null : Regex.Match(subject, rule.SubjectRegex);
            if (match is { Success: false })
            {
                continue;
            }
            return Capture().Replace(rule.Project, reference => match?.Groups[int.Parse(reference.Groups[1].Value)].Value ?? "");
        }
        return rules.UnsortedProject;
    }

    private static string SenderOf(DeliveryRules rules, string address, string domain) =>
        rules.Senders.FirstOrDefault(alias => alias.Address?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)?.Name
        ?? rules.Senders.FirstOrDefault(alias => alias.Domain?.Equals(domain, StringComparison.OrdinalIgnoreCase) == true)?.Name
        ?? domain;

    [GeneratedRegex(@"\{Capture:(\d+)\}")]
    private static partial Regex Capture();
}