using System.Text.Json;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.Cli;

internal static class GitCommands
{
    public static int Run(string[] args, ProjectConfigurationService projects, TextWriter output, TextWriter error)
    {
        if (args.Length < 2 || !Guid.TryParse(args[1], out var id) || id == Guid.Empty ||
            !(args is ["status", _] or ["switch", _, _, "local" or "remote", _] or ["fetch" or "pull", _, _, ..]))
        {
            error.WriteLine("Usage: git status <project-id> | git switch <project-id> <repository-path> local|remote <branch> | git fetch|pull <project-id> <repository-path> [repository-path ...]");
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
        GitOperationResult[] results;
        if (args[0] == "status")
            results = associated.Select(git.GetStatus).ToArray();
        else if (args[0] == "switch")
        {
            string path = args[2];
            results = [!IsAssociated(path) ? Unassociated(path)
                : args[3] == "local" ? git.SwitchLocal(path, args[4]) : git.SwitchRemote(path, args[4])];
        }
        else
        {
            results = args[2..].Select(path => !IsAssociated(path) ? Unassociated(path)
                : (args[0] == "fetch" ? git.Fetch([path]) : git.Pull([path]))[0]).ToArray();
        }

        output.WriteLine(JsonSerializer.Serialize(results, ProjectConfigurationService.JsonOptions));
        return results.All(result => result.Success) ? 0 : 1;
    }
}
