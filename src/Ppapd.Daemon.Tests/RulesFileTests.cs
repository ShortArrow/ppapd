namespace Ppapd.Daemon.Tests;

public sealed class RulesFileTests
{
    /// <summary>The example of docs/PRD.md §6.1.</summary>
    private const string Example = """
        projects_root: '\\nas01\Share\Projects'
        unsorted_project: '_Unsorted'
        download_dir: '%USERPROFILE%\Downloads\ppapd'

        senders:
          - domain: "example.co.jp"
            name: "ExampleCorp"
          - address: "someone@example.net"
            name: "ExampleNet-Someone"

        projects:
          # 件名の案件番号【P-12345】をそのままプロジェクト名にする
          - project: '{Capture:1}'
            match:
              from_domain: "example.co.jp"
              subject_regex: '【(P-\d{5})】'

          # 請求書・見積書は Accounting にまとめる
          - project: 'Accounting'
            match:
              subject_regex: '(?:請求書|見積書)'
        """;

    [Fact]
    public void The_example_in_the_requirements_loads_into_its_rules()
    {
        var settings = RulesFile.Load(new StringReader(Example));

        Assert.Equal(@"\\nas01\Share\Projects", settings.Rules.ProjectsRoot);
        Assert.Equal("_Unsorted", settings.Rules.UnsortedProject);
        Assert.Equal(
            [new SenderAlias("example.co.jp", null, "ExampleCorp"), new SenderAlias(null, "someone@example.net", "ExampleNet-Someone")],
            settings.Rules.Senders);
        Assert.Equal(
            [new ProjectRule("{Capture:1}", "example.co.jp", @"【(P-\d{5})】"), new ProjectRule("Accounting", null, "(?:請求書|見積書)")],
            settings.Rules.Projects);
    }

    [Fact]
    public void Environment_variables_in_the_folders_are_expanded()
    {
        var settings = RulesFile.Load(new StringReader(Example));

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ppapd"),
            settings.DownloadFolder.FullName);
    }

    [Fact]
    public void Senders_and_projects_may_be_left_out()
    {
        var settings = RulesFile.Load(new StringReader("""
            projects_root: 'D:\Projects'
            unsorted_project: '_Unsorted'
            download_dir: 'C:\Downloads\ppapd'
            """));

        Assert.Empty(settings.Rules.Senders);
        Assert.Empty(settings.Rules.Projects);
    }

    [Theory]
    [InlineData("projects_root")]
    [InlineData("unsorted_project")]
    [InlineData("download_dir")]
    public void A_file_missing_a_required_key_is_refused(string missing)
    {
        var text = string.Join('\n', new[]
        {
            @"projects_root: 'D:\Projects'",
            "unsorted_project: '_Unsorted'",
            @"download_dir: 'C:\Downloads\ppapd'",
        }.Where(line => !line.StartsWith(missing + ":")));

        Assert.Throws<InvalidRulesException>(() => RulesFile.Load(new StringReader(text)));
    }

    [Theory]
    [InlineData("projects_rot: 'D:\\Projects'")]
    [InlineData("senders:\n  - domain: 'example.co.jp'\n    nmae: 'ExampleCorp'")]
    [InlineData("projects:\n  - project: 'A'\n    match:\n      from_domian: 'example.co.jp'")]
    public void A_key_the_requirements_do_not_define_is_refused(string extra)
    {
        Assert.Throws<InvalidRulesException>(() => RulesFile.Load(new StringReader(Minimal + "\n" + extra)));
    }

    [Theory]
    [InlineData("unsorted_project: !!str '_Unsorted'")]
    [InlineData("unsorted_project: !<tag:yaml.org,2002:str> '_Unsorted'")]
    [InlineData("unsorted_project: !custom '_Unsorted'")]
    [InlineData("senders: !!seq\n  - domain: 'example.co.jp'\n    name: 'ExampleCorp'")]
    public void A_file_with_a_type_tag_is_refused(string tagged)
    {
        var text = $"""
            projects_root: 'D:\Projects'
            download_dir: 'C:\Downloads\ppapd'
            {tagged}
            """;

        Assert.Throws<InvalidRulesException>(() => RulesFile.Load(new StringReader(text)));
    }

    [Fact]
    public void A_file_that_is_not_yaml_is_refused()
    {
        Assert.Throws<InvalidRulesException>(() => RulesFile.Load(new StringReader("projects_root: [unclosed")));
    }

    private const string Minimal = """
        projects_root: 'D:\Projects'
        unsorted_project: '_Unsorted'
        download_dir: 'C:\Downloads\ppapd'
        """;
}
