using System.Text.Json;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.Cli;

internal static class GitCommands
{
    public static int Run(string[] args, ProjectConfigurationService projects, TextWriter output, TextWriter error)
    {
        if (args.Length < 3 || !Guid.TryParse(args[1], out var id) || id == Guid.Empty ||
            !(args is ["fetch" or "pull", _, _, ..]))
        {
            error.WriteLine("Usage: git fetch|pull <project-id> <repository-path> [repository-path ...]");
            return 2;
        }

        var loaded = projects.Get(id);
        if (loaded.Project is null || loaded.Project.Id != id)
        {
            output.WriteLine(JsonSerializer.Serialize(loaded, ProjectConfigurationService.JsonOptions));
            return 1;
        }

        var associated = loaded.Project.Repositories ?? [];
        bool IsAssociated(string path) => associated.Contains(path, OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var git = new GitRepositoryService();
        GitOperationResult Unassociated(string path) => new(path, false, "This repository path is not associated with the selected project.");
        var results = args[2..].Select(path => !IsAssociated(path) ? Unassociated(path)
            : (args[0] == "fetch" ? git.Fetch([path]) : git.Pull([path]))[0]).ToArray();

        output.WriteLine(JsonSerializer.Serialize(results, ProjectConfigurationService.JsonOptions));
        return results.All(result => result.Success) ? 0 : 1;
    }
}
