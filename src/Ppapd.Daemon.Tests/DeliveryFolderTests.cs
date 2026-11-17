namespace Ppapd.Daemon.Tests;

public sealed class DeliveryFolderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ppapd-projects");

    private static readonly DateTimeOffset November12 = new(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Local));

    /// <summary>The rules of the example in docs/PRD.md §6.1, under <see cref="Root"/>.</summary>
    private static readonly DeliveryRules Rules = new(
        ProjectsRoot: Root,
        UnsortedProject: "_Unsorted",
        Senders:
        [
            new SenderAlias(Domain: "example.co.jp", Address: null, Name: "ExampleCorp"),
            new SenderAlias(Domain: null, Address: "someone@example.net", Name: "ExampleNet-Someone"),
        ],
        Projects:
        [
            new ProjectRule(Project: "{Capture:1}", FromDomain: "example.co.jp", SubjectRegex: "【(.*?)】"),
            new ProjectRule(Project: "Accounting", FromDomain: null, SubjectRegex: "(?:請求書|見積書)"),
        ]);

    [Fact]
    public void The_example_mail_goes_under_its_project_sender_alias_and_date()
    {
        var folder = DeliveryFolder.For(Rules, new MailMetadata("taro@example.co.jp", "【P-12345】図面送付", November12)).Folder;

        Assert.Equal(Path.Combine(Root, "P-12345", "ExampleCorp", "20261112"), folder.FullName);
    }

    [Fact]
    public void A_mail_matching_no_rule_goes_under_the_unsorted_project()
    {
        var folder = DeliveryFolder.For(Rules, new MailMetadata("hanako@other.example", "ご挨拶", November12)).Folder;

        Assert.Equal(Path.Combine(Root, "_Unsorted", "other.example", "20261112"), folder.FullName);
    }

    [Fact]
    public void An_alias_for_the_address_wins_over_one_for_its_domain_and_rules_are_tried_in_order()
    {
        var folder = DeliveryFolder.For(Rules, new MailMetadata("someone@example.net", "見積書の送付", November12)).Folder;

        Assert.Equal(Path.Combine(Root, "Accounting", "ExampleNet-Someone", "20261112"), folder.FullName);
    }

    [Theory]
    [InlineData("【..】図面送付", "_")]
    [InlineData("【.】図面送付", "_")]
    [InlineData("【P-12345.】図面送付", "P-12345")]
    [InlineData("【P-12345 . 】図面送付", "P-12345")]
    [InlineData("【CON.txt】図面送付", "_CON.txt")]
    [InlineData("【con】図面送付", "_con")]
    [InlineData("【LPT9】図面送付", "_LPT9")]
    [InlineData("【COM10】図面送付", "COM10")]
    [InlineData("【A/B\\C:D*E?F\"G<H>I|J】図面送付", "A_B_C_D_E_F_G_H_I_J")]
    [InlineData("【..\\..\\Windows】図面送付", ".._.._Windows")]
    [InlineData("【】図面送付", "_")]
    public void A_project_name_from_the_subject_is_made_a_safe_folder_name_under_the_root(string subject, string project)
    {
        var folder = DeliveryFolder.For(Rules, new MailMetadata("taro@example.co.jp", subject, November12)).Folder;

        Assert.Equal(Path.Combine(Root, project, "ExampleCorp", "20261112"), folder.FullName);
    }

    [Fact]
    public void An_empty_subject_captured_whole_becomes_a_folder_named_by_the_replacement()
    {
        var rules = Rules with { Projects = [new ProjectRule(Project: "{Capture:1}", FromDomain: null, SubjectRegex: "^(.*)$")] };

        var folder = DeliveryFolder.For(rules, new MailMetadata("taro@example.co.jp", "", November12)).Folder;

        Assert.Equal(Path.Combine(Root, "_", "ExampleCorp", "20261112"), folder.FullName);
    }

    [Fact]
    public void An_empty_subject_matching_no_rule_goes_under_the_unsorted_project()
    {
        var folder = DeliveryFolder.For(Rules, new MailMetadata("taro@example.co.jp", "", November12)).Folder;

        Assert.Equal(Path.Combine(Root, "_Unsorted", "ExampleCorp", "20261112"), folder.FullName);
    }

    [Theory]
    [InlineData("x@..", "_")]
    [InlineData("x@example.co.jp.", "example.co.jp")]
    [InlineData("x@aux", "_aux")]
    public void A_sender_domain_is_made_a_safe_folder_name_too(string address, string sender)
    {
        var rules = Rules with { Senders = [new SenderAlias(Domain: "example.co.jp", Address: null, Name: "ExampleCorp")], Projects = [] };

        var folder = DeliveryFolder.For(rules, new MailMetadata(address, "件名", November12)).Folder;

        Assert.Equal(Path.Combine(Root, "_Unsorted", sender, "20261112"), folder.FullName);
    }

    [Fact]
    public void A_rule_whose_match_runs_past_the_time_limit_counts_as_not_matching_and_is_reported()
    {
        var runaway = new ProjectRule(Project: "Runaway", FromDomain: null, SubjectRegex: "^(a+)+$");
        var rules = Rules with { Projects = [runaway, .. Rules.Projects] };

        var decision = DeliveryFolder.For(rules, new MailMetadata("someone@example.net", new string('a', 40) + "!見積書", November12));

        Assert.Equal(Path.Combine(Root, "Accounting", "ExampleNet-Someone", "20261112"), decision.Folder.FullName);
        Assert.Equal([runaway], decision.TimedOut);
    }

    [Theory]
    [InlineData("taro@example.co.jp", "【P-12345】図面送付", true)]
    [InlineData("hanako@other.example", "見積書の送付", true)]
    [InlineData("hanako@other.example", "ご挨拶", false)]
    [InlineData("taro@example.co.jp", "ご挨拶", false)]
    public void A_mail_matches_when_one_of_the_project_rules_does(string from, string subject, bool matches)
    {
        Assert.Equal(matches, DeliveryFolder.Matches(Rules, new MailMetadata(from, subject, November12)));
    }

    [Fact]
    public void A_mail_matching_only_a_rule_that_ran_out_of_time_does_not_match()
    {
        var rules = Rules with { Projects = [new ProjectRule(Project: "Runaway", FromDomain: null, SubjectRegex: "^(a+)+$")] };

        Assert.False(DeliveryFolder.Matches(rules, new MailMetadata("x@example.co.jp", new string('a', 40) + "!", November12)));
    }

    [Fact]
    public void A_folder_that_resolves_outside_the_projects_root_is_refused()
    {
        var outside = Path.Combine(Root, "..", "elsewhere");

        Assert.Throws<OutsideProjectsRootException>(() => DeliveryFolder.EnsureUnderRoot(outside, Root));
    }

    [Fact]
    public void A_folder_whose_name_only_starts_like_the_projects_root_is_refused()
    {
        Assert.Throws<OutsideProjectsRootException>(() => DeliveryFolder.EnsureUnderRoot(Root + "-other", Root));
    }
}
