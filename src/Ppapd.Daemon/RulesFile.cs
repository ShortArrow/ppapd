using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ppapd.Daemon;

/// <summary>What <c>rules.yaml</c> sets (docs/PRD.md, §6).</summary>
/// <param name="Rules">The rules that decide the delivery folder.</param>
/// <param name="DownloadFolder"><c>download_dir</c>, with environment variables expanded.</param>
internal sealed record Settings(DeliveryRules Rules, DirectoryInfo DownloadFolder);

/// <summary>Reads <c>rules.yaml</c> (docs/PRD.md, §6.2).</summary>
internal static class RulesFile
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    /// <summary>
    /// The settings in <paramref name="yaml"/>, with environment variables in <c>projects_root</c> and <c>download_dir</c>
    /// expanded.
    /// </summary>
    /// <exception cref="InvalidRulesException">
    /// The text is not YAML, holds a key §6.2 does not define, holds a type tag such as <c>!!str</c> or <c>!&lt;...&gt;</c>,
    /// or leaves out a required value.
    /// </exception>
    public static Settings Load(TextReader yaml)
    {
        var text = yaml.ReadToEnd();
        try
        {
            RefuseTags(text);
            var file = Deserializer.Deserialize<FileYaml?>(text) ?? throw new InvalidRulesException("rules.yaml is empty.");
            return file.ToSettings();
        }
        catch (YamlException e)
        {
            throw new InvalidRulesException(e.Message, e);
        }
    }

    /// <summary>Refuses every explicit tag, so that nothing in the file chooses the type it is read as.</summary>
    private static void RefuseTags(string text)
    {
        var parser = new Parser(new StringReader(text));
        while (parser.MoveNext())
        {
            if (parser.Current is NodeEvent { Tag.IsEmpty: false } node)
            {
                throw new InvalidRulesException($"rules.yaml must not hold type tags ({node.Tag} at {node.Start}).");
            }
        }
    }

    private static string Required(string? value, string key) =>
        string.IsNullOrEmpty(value) ? throw new InvalidRulesException($"rules.yaml needs {key}.") : value;

    private sealed class FileYaml
    {
        public string? ProjectsRoot { get; set; }
        public string? UnsortedProject { get; set; }
        public string? DownloadDir { get; set; }
        public List<SenderYaml>? Senders { get; set; }
        public List<ProjectYaml>? Projects { get; set; }

        public Settings ToSettings() => new(
            new DeliveryRules(
                Environment.ExpandEnvironmentVariables(Required(ProjectsRoot, "projects_root")),
                Required(UnsortedProject, "unsorted_project"),
                (Senders ?? []).Select(sender => sender.ToAlias()).ToList(),
                (Projects ?? []).Select(project => project.ToRule()).ToList()),
            new DirectoryInfo(Environment.ExpandEnvironmentVariables(Required(DownloadDir, "download_dir"))));
    }

    private sealed class SenderYaml
    {
        public string? Domain { get; set; }
        public string? Address { get; set; }
        public string? Name { get; set; }

        public SenderAlias ToAlias() =>
            Domain is null && Address is null
                ? throw new InvalidRulesException("Each of senders needs domain or address.")
                : new SenderAlias(Domain, Address, Required(Name, "senders[].name"));
    }

    private sealed class ProjectYaml
    {
        public string? Project { get; set; }
        public MatchYaml? Match { get; set; }

        public ProjectRule ToRule() => new(Required(Project, "projects[].project"), Match?.FromDomain, Match?.SubjectRegex);
    }

    private sealed class MatchYaml
    {
        public string? FromDomain { get; set; }
        public string? SubjectRegex { get; set; }
    }
}

/// <summary><c>rules.yaml</c> cannot be used as it is written (docs/PRD.md, §6.2).</summary>
public sealed class InvalidRulesException(string message, Exception? inner = null) : Exception(message, inner);