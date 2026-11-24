using System.Text.Json.Serialization;

namespace DockerinoLamborgino.Core;

// Array order defines execution order. Each process is an independent session;
// every earlier step must succeed; an optional explicit earlier-step dependency
// is retained for compatibility with previously saved configurations.
public sealed class ProjectConfiguration
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Directory { get; set; } = "";
    // Explicit paths only; older project files without this property have no associations.
    public List<string> Repositories { get; set; } = [];
    [JsonRequired]
    public List<LaunchAction> Actions { get; set; } = [];
    [JsonIgnore]
    public IReadOnlyList<LaunchAction> QuickLaunchActions =>
        (Actions ?? []).Where(action => action?.IsQuickLaunch == true).ToArray();
}

public sealed class LaunchAction
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsQuickLaunch { get; set; }
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

public sealed record ConfigurationIssue(string Path, string Message)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsResourceAvailability { get; init; }
}
public sealed record ProjectLoadResult(string Source, ProjectConfiguration? Project, IReadOnlyList<ConfigurationIssue> Issues, string? Revision = null);
