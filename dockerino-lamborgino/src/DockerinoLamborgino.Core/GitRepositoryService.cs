using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;

namespace DockerinoLamborgino.Core;

public sealed record GitRepositoryState(string Path, string? CurrentBranch, string WorkingTreeStatus,
    IReadOnlyList<string> LocalBranches, IReadOnlyList<string> RemoteBranches);

public sealed record GitOperationResult(string Path, bool Success, string Message, GitRepositoryState? State = null);

// Callers supply only paths explicitly associated with the selected project.
public sealed class GitRepositoryService
{
    public GitOperationResult GetStatus(string path)
    {
        try
        {
            var error = Validate(path);
            if (error is not null) return new(path, false, error);
            var branch = Run(path, "symbolic-ref", "--quiet", "--short", "HEAD");
            var status = Run(path, "status", "--porcelain=v1", "--untracked-files=all");
            var local = Run(path, "for-each-ref", "--format=%(refname:short)", "refs/heads/");
            var remote = Run(path, "for-each-ref", "--format=%(refname:short)%09%(symref)", "refs/remotes/");
            foreach (var result in new[] { status, local, remote })
                if (result.ExitCode != 0) return new(path, false, result.Error);
            var remotes = Lines(remote.Output).Select(x => x.Split('\t'))
                .Where(x => x.Length == 1 || x[1].Length == 0).Select(x => x[0]).ToArray();
            return new(path, true, "", new(path, branch.ExitCode == 0 ? branch.Output.TrimEnd('\r', '\n') : null,
                status.Output, Lines(local.Output), remotes));
        }
        catch (Exception ex) when (IsExpected(ex)) { return new(path, false, ex.Message); }
    }

    public GitOperationResult SwitchLocal(string path, string branch)
    {
        var state = GetStatus(path);
        if (!state.Success) return state;
        if (!state.State!.LocalBranches.Contains(branch, StringComparer.Ordinal))
            return new(path, false, "The selected local branch does not exist.", state.State);
        return Complete(path, () => Run(path, "switch", "--no-guess", "--", branch));
    }

    public GitOperationResult SwitchRemote(string path, string remoteBranch)
    {
        var state = GetStatus(path);
        if (!state.Success) return state;
        if (!state.State!.RemoteBranches.Contains(remoteBranch, StringComparer.Ordinal))
            return new(path, false, "The selected remote branch does not exist.", state.State);
        // Resolve the remote name from the actual configured remotes, not from a guessed prefix.
        var remotes = Run(path, "remote");
        if (remotes.ExitCode != 0) return new(path, false, remotes.Error, state.State);
        var matches = Lines(remotes.Output).Where(r => remoteBranch.StartsWith(r + "/", StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) return new(path, false, "The remote branch name is ambiguous.", state.State);
        string local = remoteBranch[(matches[0].Length + 1)..];
        if (state.State.LocalBranches.Contains(local, StringComparer.Ordinal))
        {
            var upstream = Run(path, "for-each-ref", "--format=%(upstream:short)", "refs/heads/" + local);
            if (upstream.ExitCode != 0 || upstream.Output.Trim() != remoteBranch)
                return new(path, false, "A local branch with this name exists with a different or missing upstream.", state.State);
            return SwitchLocal(path, local);
        }
        return Complete(path, () => Run(path, "switch", "--track", "-c", local, "--", "refs/remotes/" + remoteBranch));
    }

    public IReadOnlyList<GitOperationResult> Fetch(IEnumerable<string> paths) =>
        paths.Select(path => Network(path, false)).ToArray();

    public IReadOnlyList<GitOperationResult> Pull(IEnumerable<string> paths) =>
        paths.Select(path => Network(path, true)).ToArray();

    private GitOperationResult Network(string path, bool pull)
    {
        var state = GetStatus(path);
        if (!state.Success) return state;
        try
        {
            string? branch = state.State!.CurrentBranch;
            var configuredRemote = branch is null ? null : Run(path, "config", "--get", $"branch.{branch}.remote");
            var configuredMerge = branch is null ? null : Run(path, "config", "--get", $"branch.{branch}.merge");
            bool hasUpstream = configuredRemote is { ExitCode: 0 } && !string.IsNullOrWhiteSpace(configuredRemote.Output)
                && configuredMerge is { ExitCode: 0 } && !string.IsNullOrWhiteSpace(configuredMerge.Output);
            if (pull)
            {
                if (!hasUpstream) return new(path, false, "Pull requires a configured upstream branch.", state.State);
                var result = Complete(path, () => Run(path, "-c", "pull.rebase=false", "-c", "merge.autoStash=false",
                    "-c", "rebase.autoStash=false", "pull", "--ff-only", "--no-recurse-submodules"));
                return NetworkMessage(result);
            }
            string? remote = null;
            if (hasUpstream)
            {
                remote = configuredRemote!.Output.Trim();
                if (string.IsNullOrEmpty(remote))
                    return new(path, false, "Cannot determine the upstream remote.", state.State);
            }
            else
            {
                var configured = Run(path, "remote");
                if (configured.ExitCode != 0) return new(path, false, configured.Error, state.State);
                var names = Lines(configured.Output);
                if (names.Length != 1)
                    return new(path, false, names.Length == 0 ? "No remote is configured." : "Multiple remotes are configured; fetch is ambiguous.", state.State);
                remote = names[0];
            }
            if (remote == ".") return new(path, false, "The upstream is local; there is no remote to fetch.", state.State);
            return NetworkMessage(Complete(path, () => Run(path, "fetch", "--no-recurse-submodules", "--", remote)));
        }
        catch (Exception ex) when (IsExpected(ex)) { return new(path, false, ex.Message, state.State); }
    }

    private static GitOperationResult NetworkMessage(GitOperationResult result) => result.Success ? result :
        result with { Message = result.Message + " Git runs noninteractively; interactive authentication prompts are not supported." };

    private GitOperationResult Complete(string path, Func<CommandResult> command)
    {
        try
        {
            var result = command();
            var refreshed = GetStatus(path);
            return new(path, result.ExitCode == 0, result.ExitCode == 0
                ? (result.Output + result.Error).Trim() : result.Error, refreshed.State);
        }
        catch (Exception ex) when (IsExpected(ex)) { return new(path, false, ex.Message, GetStatus(path).State); }
    }

    private static string? Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !Directory.Exists(path))
            return "An existing absolute repository directory is required.";
        var root = Run(path, "rev-parse", "--show-toplevel");
        if (root.ExitCode != 0) return "Not a Git working-tree repository: " + root.Error;
        if (!string.Equals(NormalizePath(path),
                NormalizePath(root.Output.TrimEnd('\r', '\n')),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return "The associated path must be the repository root, not a directory inside another repository.";
        return null;
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));

    private static string[] Lines(string output) => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    private static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException
        or InvalidOperationException or Win32Exception or NotSupportedException;

    private sealed record CommandResult(int ExitCode, string Output, string Error);
    private static CommandResult Run(string path, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = path, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "Never";
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start Git.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.WaitForExit();
            return new(-1, "", "Git timed out after 30 seconds; authentication requiring interaction is not supported.");
        }
        string stderr = Redact(error.GetAwaiter().GetResult()).Trim();
        return new(process.ExitCode, Redact(output.GetAwaiter().GetResult()), stderr.Length == 0 && process.ExitCode != 0
            ? $"Git exited with code {process.ExitCode}." : stderr);
    }

    private static string Redact(string text) =>
        Regex.Replace(text, @"(?i)([a-z][a-z0-9+.-]*://)[^/\s]+@", "$1[redacted]@");
}
