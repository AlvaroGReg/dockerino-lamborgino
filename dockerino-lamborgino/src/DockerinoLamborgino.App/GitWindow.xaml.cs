using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.App;

// UI-facing contract: the Core integration maps its own models and errors here.
public sealed record GitBranchEntry(string Name, bool IsRemote);
public sealed record GitRepositoryEntry(string Path, string CurrentBranch, string Status,
    IReadOnlyList<GitBranchEntry> LocalBranches, IReadOnlyList<GitBranchEntry> RemoteBranches, string? Error = null)
{
    public string Summary => Error is null ? $"Branch: {CurrentBranch}" : $"Error: {Error}";
}
public sealed record GitOperationResult(string RepositoryPath, bool Succeeded, string Message);
public interface IGitWindowAdapter
{
    Task<IReadOnlyList<GitRepositoryEntry>> ListAsync(string projectId);
    Task<GitOperationResult> SwitchAsync(string projectId, string repositoryPath, GitBranchEntry branch);
    Task<IReadOnlyList<GitOperationResult>> FetchAsync(string projectId, IReadOnlyList<string> repositoryPaths);
    Task<IReadOnlyList<GitOperationResult>> PullAsync(string projectId, IReadOnlyList<string> repositoryPaths);
}

internal sealed class CoreGitWindowAdapter(IProjectConfigurationAdapter projects) : IGitWindowAdapter
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

    public Task<IReadOnlyList<GitRepositoryEntry>> ListAsync(string projectId) => Task.Run<IReadOnlyList<GitRepositoryEntry>>(() =>
        Paths(projectId).Select(path =>
        {
            var result = service.GetStatus(path);
            var state = result.State;
            return new GitRepositoryEntry(path, state?.CurrentBranch ?? "(detached HEAD)",
                string.IsNullOrWhiteSpace(state?.WorkingTreeStatus) ? "Clean working tree." : state.WorkingTreeStatus,
                state?.LocalBranches.Select(b => new GitBranchEntry(b, false)).ToArray() ?? [],
                state?.RemoteBranches.Select(b => new GitBranchEntry(b, true)).ToArray() ?? [],
                result.Success ? null : result.Message);
        }).ToArray());

    public Task<GitOperationResult> SwitchAsync(string projectId, string repositoryPath, GitBranchEntry branch) => Task.Run(() =>
    {
        EnsureAssociated(projectId, [repositoryPath]);
        var result = branch.IsRemote ? service.SwitchRemote(repositoryPath, branch.Name) : service.SwitchLocal(repositoryPath, branch.Name);
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

public partial class GitWindow : Window
{
    private readonly IGitWindowAdapter git;
    private bool busy;
    private bool updating;

    public GitWindow(IProjectConfigurationAdapter projects, IGitWindowAdapter git, string? selectedProjectId = null)
    {
        this.git = git;
        InitializeComponent();
        try
        {
            var result = projects.List();
            Projects.ItemsSource = result.Projects;
            Projects.SelectedItem = result.Projects.FirstOrDefault(project => project.Id == selectedProjectId);
            Results.Text = string.Join(Environment.NewLine, result.Errors);
        }
        catch (Exception ex) { Results.Text = ex.Message; }
        UpdateButtons();
    }

    private string? ProjectId => (Projects.SelectedItem as ProjectConfigurationEntry)?.Id;
    private GitRepositoryEntry? SingleRepository => Repositories.SelectedItems.Count == 1 ? Repositories.SelectedItem as GitRepositoryEntry : null;
    private GitBranchEntry? SelectedBranch => LocalBranches.SelectedItem as GitBranchEntry ?? RemoteBranches.SelectedItem as GitBranchEntry;

    private async void Projects_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Results.Clear();
        Repositories.ItemsSource = null;
        ShowRepository();
        await RunAsync(RefreshRepositoriesAsync);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshRepositoriesAsync);
    private async void Fetch_Click(object sender, RoutedEventArgs e) => await BatchAsync(false);
    private async void Pull_Click(object sender, RoutedEventArgs e) => await BatchAsync(true);

    private async Task BatchAsync(bool pull)
    {
        var id = ProjectId;
        var paths = Repositories.SelectedItems.Cast<GitRepositoryEntry>().Select(r => r.Path).ToArray();
        if (id is null || paths.Length == 0) return;
        await RunAsync(async () =>
        {
            try
            {
                var results = pull ? await git.PullAsync(id, paths) : await git.FetchAsync(id, paths);
                Results.Text = string.Join(Environment.NewLine, results.Select(r => $"{r.RepositoryPath}: {(r.Succeeded ? "OK" : "ERROR")} — {r.Message}"));
            }
            finally { await RefreshRepositoriesAsync(); }
        });
    }

    private async void Switch_Click(object sender, RoutedEventArgs e)
    {
        var id = ProjectId;
        var repository = SingleRepository;
        var branch = SelectedBranch;
        if (id is null || repository is null || branch is null) return;
        await RunAsync(async () =>
        {
            try
            {
                var result = await git.SwitchAsync(id, repository.Path, branch);
                Results.Text = $"{result.RepositoryPath}: {(result.Succeeded ? "OK" : "ERROR")} — {result.Message}";
            }
            finally { await RefreshRepositoriesAsync(); }
        });
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (busy) return;
        busy = true;
        UpdateButtons();
        try { await operation(); }
        catch (Exception ex) { Results.Text = string.IsNullOrEmpty(Results.Text) ? ex.Message : $"{Results.Text}{Environment.NewLine}{ex.Message}"; }
        finally { busy = false; UpdateButtons(); }
    }

    private async Task RefreshRepositoriesAsync()
    {
        var id = ProjectId;
        var selected = Repositories.SelectedItems.Cast<GitRepositoryEntry>().Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = id is null ? [] : await git.ListAsync(id);
        updating = true;
        try
        {
            Repositories.ItemsSource = entries;
            foreach (var item in entries.Where(r => selected.Contains(r.Path))) Repositories.SelectedItems.Add(item);
        }
        finally { updating = false; }
        ShowRepository();
    }

    private void Repositories_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updating) ShowRepository();
    }

    private void ShowRepository()
    {
        var repository = SingleRepository;
        Status.Text = repository is null ? "Select one repository to view its status." : repository.Error ?? repository.Status;
        LocalBranches.ItemsSource = repository?.LocalBranches;
        RemoteBranches.ItemsSource = repository?.RemoteBranches;
        UpdateButtons();
    }

    private void LocalBranches_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocalBranches.SelectedItem is not null) RemoteBranches.SelectedItem = null;
        UpdateButtons();
    }
    private void RemoteBranches_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RemoteBranches.SelectedItem is not null) LocalBranches.SelectedItem = null;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (Projects is null || Repositories is null || SwitchButton is null) return;
        Projects.IsEnabled = !busy;
        Repositories.IsEnabled = !busy;
        LocalBranches.IsEnabled = !busy;
        RemoteBranches.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy && ProjectId is not null;
        FetchButton.IsEnabled = PullButton.IsEnabled = !busy && ProjectId is not null && Repositories.SelectedItems.Count > 0;
        SwitchButton.IsEnabled = !busy && SingleRepository is not null && SelectedBranch is not null;
    }
}
