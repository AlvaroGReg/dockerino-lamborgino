using System.Diagnostics;
using System.Text.Json;
using DockerinoLamborgino.Cli;
using DockerinoLamborgino.Core;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public sealed class GitCliTests
{
    [Fact]
    public void FetchAndPullBatchUseOnlyAssociatedPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "dockerino git cli " + Guid.NewGuid());
        string repo = Path.Combine(root, "external repository");
        string other = Path.Combine(root, "other");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(other);
        try
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = repo, UseShellExecute = false };
            start.ArgumentList.Add("init");
            using var process = Process.Start(start)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            var store = new ProjectConfigurationService(Path.Combine(root, "store"));
            var project = ProjectConfigurationService.Create("project", root);
            project.Repositories.Add(repo);
            Assert.Empty(store.Save(project));
            var output = new StringWriter();
            Assert.Equal(2, Program.Run(["git", "status", project.Id.ToString()], output, new StringWriter(), store.StorageDirectory));
            Assert.Equal(2, Program.Run(["git", "switch", project.Id.ToString(), repo, "local", "main"], output, new StringWriter(), store.StorageDirectory));
            output = new StringWriter();
            Assert.Equal(1, Program.Run(["git", "fetch", project.Id.ToString(), other, repo], output, new StringWriter(), store.StorageDirectory));
            var batch = JsonSerializer.Deserialize<GitOperationResult[]>(output.ToString(), ProjectConfigurationService.JsonOptions)!;
            Assert.Equal(2, batch.Length);
            Assert.Contains("not associated", batch[0].Message);
            Assert.Contains("remote", batch[1].Message, StringComparison.OrdinalIgnoreCase);
            output = new StringWriter();
            Assert.Equal(1, Program.Run(["git", "pull", project.Id.ToString(), repo], output, new StringWriter(), store.StorageDirectory));
            var pull = JsonSerializer.Deserialize<GitOperationResult[]>(output.ToString(), ProjectConfigurationService.JsonOptions)!;
            Assert.Single(pull);
            Assert.Contains("upstream", pull[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }
}
