namespace Ppapd.Daemon;

/// <summary>The rules of <c>rules.yaml</c> that decide the delivery folder (docs/PRD.md, §3 and §6.2).</summary>
/// <param name="ProjectsRoot"><c>projects_root</c>: the folder every delivery folder is made under.</param>
/// <param name="UnsortedProject"><c>unsorted_project</c>: the project name when no rule matches.</param>
/// <param name="Senders"><c>senders</c>: aliases used as sender names.</param>
/// <param name="Projects"><c>projects</c>: tried from the top; the first that matches names the project.</param>
internal sealed record DeliveryRules(
    string ProjectsRoot,
    string UnsortedProject,
    IReadOnlyList<SenderAlias> Senders,
    IReadOnlyList<ProjectRule> Projects);

/// <summary>One entry of <c>senders</c>; an <paramref name="Address"/> match wins over a <paramref name="Domain"/> match.</summary>
internal sealed record SenderAlias(string? Domain, string? Address, string Name);

/// <summary>
/// One entry of <c>projects</c>. It matches when every condition given holds; <paramref name="Project"/> may refer to the
/// n-th capture of <paramref name="SubjectRegex"/> as <c>{Capture:n}</c>.
/// </summary>
internal sealed record ProjectRule(string Project, string? FromDomain, string? SubjectRegex);

/// <summary>What the delivery folder is decided from, taken from the mail that holds the file or its link.</summary>
internal sealed record MailMetadata(string SenderAddress, string Subject, DateTimeOffset Received)
{
    /// <summary>What follows the last <c>@</c> of <see cref="SenderAddress"/>, or all of it when it has none.</summary>
    public string SenderDomain => SenderAddress[(SenderAddress.LastIndexOf('@') + 1)..];
}