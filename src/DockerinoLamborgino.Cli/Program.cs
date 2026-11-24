using System.Text.Json;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.Cli;

public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter output, TextWriter error, string? storageDirectory = null)
    {
        if (args is ["--version"])
        {
            output.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");
            return 0;
        }

        if (args is ["--help"])
        {
            output.WriteLine("""
                Usage: dockerino <command>

                Projects and configuration:
                  register <name> <absolute-directory>
                  list
                  get <project-id>
                  validate <json-file>
                  save <json-file> [expected-revision]
                  projects refresh [project-id]

                Git repositories:
                  git fetch|pull <project-id> <repository-path> [repository-path ...]

                Launches and sessions:
                  launch start <project-id> <action-id>
                  launch list
                  launch inspect|stop <launch-id>
                  session list
                  session inspect|stop|reset <session-id>
                  session output|follow <session-id> [--offset <offset>]
                  reset
                  dashboard open

                Options:
                  --help  Show this help.
                  --version  Show the CLI version.
                """);
            return 0;
        }

        var service = new ProjectConfigurationService(storageDirectory);
        void Print(object value) => output.WriteLine(JsonSerializer.Serialize(value, ProjectConfigurationService.JsonOptions));
        try
        {
            switch (args)
            {
                case ["list"]:
                    Print(service.List());
                    return 0;
                case ["get", var id] when Guid.TryParse(id, out var projectId) && projectId != Guid.Empty:
                    var result = service.Get(projectId);
                    Print(result);
                    return result.Project is null || result.Issues.Count != 0 ? 1 : 0;
                case ["register", var name, var directory]:
                    var project = ProjectConfigurationService.Create(name, directory);
                    var registrationIssues = service.Save(project);
                    if (registrationIssues.Count > 0) { Print(registrationIssues); return 1; }
                    Print(service.Get(project.Id));
                    return 0;
                case ["validate", var file]:
                    var loaded = service.LoadFile(file);
                    Print(loaded);
                    return loaded.Issues.Count == 0 ? 0 : 1;
                case ["save", var file]:
                    return Save(file, null);
                case ["save", var file, var revision]:
                    return Save(file, revision);
                case ["git", ..]:
                    return GitCommands.Run(args[1..], service, output, error);
                case ["launch" or "session" or "dashboard" or "projects" or "reset", ..]:
                    return LocalCommands.Run(args, output, error);
                default:
                    error.WriteLine("Usage: list | get <id> | register <name> <absolute-directory> | validate <json-file> | save <json-file> [expected-revision] | git fetch|pull <project-id> <repository-path> [repository-path ...] | projects refresh [project-id] | launch start|list|inspect|stop ... | session list | session inspect|stop|output|follow ... | reset | dashboard open");
                    return 2;
            }

            int Save(string file, string? revision)
            {
                var loaded = service.LoadFile(file);
                if (loaded.Project is null || loaded.Issues.Count != 0) { Print(loaded); return 1; }
                var issues = service.Save(loaded.Project, revision);
                if (issues.Count != 0) { Print(issues); return 1; }
                Print(service.Get(loaded.Project.Id));
                return 0;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }
}
