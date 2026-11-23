using System.Diagnostics;
using DockerinoLamborgino.Core;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public sealed class GitRepositoryServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dockerino git " + Guid.NewGuid());
    private readonly GitRepositoryService service = new();

    public GitRepositoryServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public void StatusSwitchAndRejectParentDiscovery()
    {
        string repo = Init("repository");
        Git(repo, "checkout", "-b", "other");
        Git(repo, "checkout", "main");
        File.WriteAllText(Path.Combine(repo, "untracked"), "changed");
        var status = service.GetStatus(repo);
        Assert.True(status.Success, status.Message);
        Assert.Equal("main", status.State!.CurrentBranch);
        Assert.Contains("other", status.State.LocalBranches);
        Assert.Contains("untracked", status.State.WorkingTreeStatus);
        Assert.True(service.SwitchLocal(repo, "other").Success);
        Assert.False(service.SwitchLocal(repo, "--force").Success);
        string child = Path.Combine(repo, "child");
        Directory.CreateDirectory(child);
        Assert.False(service.GetStatus(child).Success);
        Assert.True(File.Exists(Path.Combine(repo, "untracked")));
    }

    [Fact]
    public void FetchRulesTrackingAndBatchFailures()
    {
        string origin = Path.Combine(root, "origin.git");
        Git(root, "init", "--bare", origin);
        string repo = Init("repo");
        Git(repo, "remote", "add", "origin", origin);
        Git(repo, "push", "-u", "origin", "main");
        Git(repo, "checkout", "-b", "feature");
        Git(repo, "push", "-u", "origin", "feature");
        Git(repo, "checkout", "main");
        Git(repo, "branch", "-D", "feature");
        var fetched = service.Fetch([Path.Combine(root, "missing"), repo]);
        Assert.False(fetched[0].Success);
        Assert.True(fetched[1].Success, fetched[1].Message);
        Assert.Contains("origin/feature", fetched[1].State!.RemoteBranches);
        var switched = service.SwitchRemote(repo, "origin/feature");
        Assert.True(switched.Success, switched.Message);
        Assert.Equal("feature", switched.State!.CurrentBranch);
        Git(repo, "checkout", "--orphan", "untracked-branch");
        Assert.False(service.Pull([repo])[0].Success);
        Assert.True(service.Fetch([repo])[0].Success);
        Git(repo, "remote", "add", "second", origin);
        Assert.False(service.Fetch([repo])[0].Success);
        Git(repo, "remote", "remove", "origin");
        Git(repo, "remote", "remove", "second");
        Assert.False(service.Fetch([repo])[0].Success);
    }

    [Fact]
    public void PullFastForwardsAndRejectsDivergence()
    {
        string origin = Path.Combine(root, "origin.git");
        Git(root, "init", "--bare", origin);
        string first = Init("first");
        Git(first, "remote", "add", "origin", origin);
        Git(first, "push", "-u", "origin", "main");
        string second = Path.Combine(root, "second");
        Git(root, "clone", "--branch", "main", origin, second);
        Git(second, "config", "user.name", "Test");
        Git(second, "config", "user.email", "test@example.com");
        Commit(first, "one");
        Git(first, "push");
        var fastForward = service.Pull([second])[0];
        Assert.True(fastForward.Success, fastForward.Message);
        Commit(first, "remote");
        Git(first, "push");
        Commit(second, "local");
        var divergent = service.Pull([second])[0];
        Assert.False(divergent.Success);
        Assert.False(File.Exists(Path.Combine(second, ".git", "MERGE_HEAD")));
        Assert.Equal("local", File.ReadAllText(Path.Combine(second, "local")));
    }

    [Fact]
    public void SwitchPreservesConflictingChanges()
    {
        string repo = Init("conflict");
        Git(repo, "checkout", "-b", "other");
        File.WriteAllText(Path.Combine(repo, "initial"), "other");
        Git(repo, "commit", "-am", "other");
        Git(repo, "checkout", "main");
        File.WriteAllText(Path.Combine(repo, "initial"), "local changes");
        var result = service.SwitchLocal(repo, "other");
        Assert.False(result.Success);
        Assert.Equal("main", result.State!.CurrentBranch);
        Assert.Equal("local changes", File.ReadAllText(Path.Combine(repo, "initial")));
    }

    [Fact]
    public void UpstreamSelectsRemoteAndTrackingConflictIsRejected()
    {
        string origin = Path.Combine(root, "origin.git");
        Git(root, "init", "--bare", origin);
        string repo = Init("multi");
        Git(repo, "remote", "add", "origin", origin);
        Git(repo, "push", "-u", "origin", "main");
        Git(repo, "checkout", "-b", "feature");
        Git(repo, "push", "-u", "origin", "feature");
        Git(repo, "checkout", "main");
        Git(repo, "branch", "--unset-upstream", "feature");
        Git(repo, "remote", "add", "bad", Path.Combine(root, "nonexistent.git"));
        var fetched = service.Fetch([repo])[0];
        Assert.True(fetched.Success, fetched.Message);
        var switched = service.SwitchRemote(repo, "origin/feature");
        Assert.False(switched.Success);
        Assert.Equal("main", switched.State!.CurrentBranch);
        Git(repo, "checkout", "--orphan", "no-upstream");
        Git(repo, "remote", "remove", "origin");
        var failure = service.Fetch([repo])[0];
        Assert.False(failure.Success);
        Assert.Contains("noninteractive", failure.Message);
    }

    private string Init(string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "-b", "main");
        Git(path, "config", "user.name", "Test");
        Git(path, "config", "user.email", "test@example.com");
        Commit(path, "initial");
        return path;
    }

    private static void Commit(string path, string name)
    {
        File.WriteAllText(Path.Combine(path, name), name);
        Git(path, "add", name);
        Git(path, "commit", "-m", name);
    }

    private static void Git(string path, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = path, RedirectStandardError = true,
            RedirectStandardOutput = true, UseShellExecute = false };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{string.Join(" ", args)}: {stderr.Result} {stdout.Result}");
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
