using System.Text.Json;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.App;

public sealed record GitBranchEntry(string Name, bool IsRemote);
public sealed record GitRepositoryEntry(string Path, string CurrentBranch, string Status,
    IReadOnlyList<GitBranchEntry> LocalBranches, IReadOnlyList<GitBranchEntry> RemoteBranches, string? Error = null);
public sealed record GitOperationResult(string RepositoryPath, bool Succeeded, string Message);

public interface IGitRepositoryAdapter
{
    Task<IReadOnlyList<GitRepositoryEntry>> ListAsync(string projectId);
    Task<GitOperationResult> SwitchAsync(string projectId, string repositoryPath, GitBranchEntry branch);
    Task<IReadOnlyList<GitOperationResult>> FetchAsync(string projectId, IReadOnlyList<string> repositoryPaths);
    Task<IReadOnlyList<GitOperationResult>> PullAsync(string projectId, IReadOnlyList<string> repositoryPaths);
}

internal sealed class CoreGitRepositoryAdapter(IProjectConfigurationAdapter projects) : IGitRepositoryAdapter
{
    private readonly GitRepositoryService service = new();

    private IReadOnlyList<string> Paths(string id) =>
        JsonSerializer.Deserialize<List<string>>(projects.Load(id).RepositoriesJson)
        ?? throw new InvalidOperationException("The project's repository list is invalid.");

    private void EnsureAssociated(string id, IEnumerable<string> paths)
    {
        var associated = Paths(id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (paths.Any(path => !associated.Contains(path)))
            throw new InvalidOperationException("A selected repository is no longer associated with this project. Refresh and try again.");
    }

    public Task<IReadOnlyList<GitRepositoryEntry>> ListAsync(string projectId) =>
        Task.Run<IReadOnlyList<GitRepositoryEntry>>(() =>
            Paths(projectId).Select(path =>
            {
                var result = service.GetStatus(path);
                var state = result.State;
                return new GitRepositoryEntry(path, state?.CurrentBranch ?? "(detached HEAD)",
                    string.IsNullOrWhiteSpace(state?.WorkingTreeStatus) ? "Clean working tree." : state.WorkingTreeStatus,
                    state?.LocalBranches.Select(branch => new GitBranchEntry(branch, false)).ToArray() ?? [],
                    state?.RemoteBranches.Select(branch => new GitBranchEntry(branch, true)).ToArray() ?? [],
                    result.Success ? null : result.Message);
            }).ToArray());

    public Task<GitOperationResult> SwitchAsync(string projectId, string repositoryPath, GitBranchEntry branch) =>
        Task.Run(() =>
        {
            EnsureAssociated(projectId, [repositoryPath]);
            var result = branch.IsRemote
                ? service.SwitchRemote(repositoryPath, branch.Name)
                : service.SwitchLocal(repositoryPath, branch.Name);
            return Convert(result);
        });

    public Task<IReadOnlyList<GitOperationResult>> FetchAsync(string projectId, IReadOnlyList<string> repositoryPaths) =>
        BatchAsync(projectId, repositoryPaths, false);

    public Task<IReadOnlyList<GitOperationResult>> PullAsync(string projectId, IReadOnlyList<string> repositoryPaths) =>
        BatchAsync(projectId, repositoryPaths, true);

    private Task<IReadOnlyList<GitOperationResult>> BatchAsync(string id, IReadOnlyList<string> paths, bool pull) =>
        Task.Run<IReadOnlyList<GitOperationResult>>(() =>
        {
            EnsureAssociated(id, paths);
            return (pull ? service.Pull(paths) : service.Fetch(paths)).Select(Convert).ToArray();
        });

    private static GitOperationResult Convert(DockerinoLamborgino.Core.GitOperationResult result) =>
        new(result.Path, result.Success, result.Message);
}
