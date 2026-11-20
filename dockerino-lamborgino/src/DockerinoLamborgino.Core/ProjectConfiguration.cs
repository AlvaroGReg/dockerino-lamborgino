using System.Text.Json.Serialization;

namespace DockerinoLamborgino.Core;

// Array order defines execution order. Each process is an independent session;
// a step may depend on the success of an earlier step in that same session.
public sealed class ProjectConfiguration
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Directory { get; set; } = "";
    [JsonRequired]
    public List<LaunchAction> Actions { get; set; } = [];
}

public sealed class LaunchAction
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Description { get; set; } = "";
    public List<LaunchProcess> Processes { get; set; } = [];
}

public sealed class LaunchProcess
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public List<LaunchStep> Steps { get; set; } = [];
}

public sealed class LaunchStep
{
    public Guid Id { get; set; }
    public string Command { get; set; } = "";
    public Guid? DependsOnSuccessOf { get; set; }
}

public sealed record ConfigurationIssue(string Path, string Message);
public sealed record ProjectLoadResult(string Source, ProjectConfiguration? Project, IReadOnlyList<ConfigurationIssue> Issues, string? Revision = null);
