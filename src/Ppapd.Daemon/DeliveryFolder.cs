using System.Text.RegularExpressions;

namespace Ppapd.Daemon;

/// <summary>Decides where a mail's files are delivered (docs/PRD.md, §3).</summary>
internal static partial class DeliveryFolder
{
    /// <summary>
    /// <c>&lt;projects root&gt;\&lt;project&gt;\&lt;sender&gt;\&lt;received yyyyMMdd&gt;</c> for <paramref name="mail"/>,
    /// with the project and sender names made safe as §6.2 sets.
    /// </summary>
    /// <returns>The folder, and the rules whose subject match ran past <see cref="MatchTimeout"/> and so counted as not matching.</returns>
    /// <exception cref="OutsideProjectsRootException">The folder does not resolve under the projects root.</exception>
    public static DeliveryDecision For(DeliveryRules rules, MailMetadata mail)
    {
        var domain = DomainOf(mail.SenderAddress);
        var timedOut = new List<ProjectRule>();
        var path = Path.Combine(
            rules.ProjectsRoot,
            FolderName.Safe(ProjectOf(rules, domain, mail.Subject, timedOut)),
            FolderName.Safe(SenderOf(rules, mail.SenderAddress, domain)),
            mail.Received.ToLocalTime().ToString("yyyyMMdd"));
        return new DeliveryDecision(EnsureUnderRoot(path, rules.ProjectsRoot), timedOut);
    }

    /// <summary>How long one rule's <c>subject_regex</c> may run on a subject (docs/PRD.md, §6.2).</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

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

    private static string ProjectOf(DeliveryRules rules, string domain, string subject, List<ProjectRule> timedOut)
    {
        foreach (var rule in rules.Projects)
        {
            if (rule.FromDomain is not null && !rule.FromDomain.Equals(domain, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Match? match;
            try
            {
                match = rule.SubjectRegex is null ? null : Regex.Match(subject, rule.SubjectRegex, RegexOptions.None, MatchTimeout);
            }
            catch (RegexMatchTimeoutException)
            {
                timedOut.Add(rule);
                continue;
            }
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

/// <summary>Where a mail's files go, and the rules whose subject match ran out of time on the way, for the error record.</summary>
internal sealed record DeliveryDecision(DirectoryInfo Folder, IReadOnlyList<ProjectRule> TimedOut);