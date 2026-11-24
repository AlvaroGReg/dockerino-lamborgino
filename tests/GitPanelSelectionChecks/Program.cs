using System.Windows;
using System.Windows.Controls;
using DockerinoLamborgino.App;
using DockerinoLamborgino.Core;
using GitOperationResult = DockerinoLamborgino.App.GitOperationResult;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        SwitchSelection(false);
        SwitchSelection(true);
        ClearSelection(false);
        ClearSelection(true);
        ReturnToSameProject();
        CurrentErrorAndSelectionReset();
        Console.WriteLine("Git panel selection checks passed.");
    }

    private static ProjectConfiguration Project(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, Directory = $@"C:\{name}",
        Repositories = [$@"C:\{name}"]
    };

    private static GitRepositoryEntry Entry(string name) =>
        new($@"C:\{name}", "main", "Clean", [], []);

    private static void SwitchSelection(bool failOld)
    {
        var adapter = new DelayedAdapter();
        var panel = new GitRepositoriesPanel();
        panel.Initialize(adapter);
        var a = Project("A");
        var b = Project("B");
        panel.SetProject(a);
        panel.SetProject(b);
        Complete(adapter.Requests[0], failOld, "A");
        Check(adapter.Requests.Count == 2 && adapter.Requests[1].Id == b.Id.ToString(), "B refresh was lost");
        Check(Rows(panel).Items.Count == 0 && Summary(panel).ToolTip is null, "stale A was published");
        Complete(adapter.Requests[1], false, "B");
        Check(Rows(panel).Items.Count == 1 && Summary(panel).Text == "1 repositories", "B results missing");
        Check(Rows(panel).Items[0].GetType().GetProperty("Path")!.GetValue(Rows(panel).Items[0])!.Equals(@"C:\B"),
            "wrong repository displayed");
    }

    private static void ClearSelection(bool failOld)
    {
        var adapter = new DelayedAdapter();
        var panel = new GitRepositoriesPanel();
        panel.Initialize(adapter);
        panel.SetProject(Project("A"));
        panel.SetProject(null);
        Complete(adapter.Requests[0], failOld, "A");
        Check(adapter.Requests.Count == 1 && Rows(panel).Items.Count == 0, "cleared selection populated");
        Check(Summary(panel).Text == "Select a project" && Summary(panel).ToolTip is null, "stale diagnostic published");
        Check(((TextBlock)panel.FindName("NoRepositoriesText")).Visibility == Visibility.Collapsed, "empty state displayed");
    }

    private static void ReturnToSameProject()
    {
        var adapter = new DelayedAdapter();
        var panel = new GitRepositoriesPanel();
        panel.Initialize(adapter);
        var a = Project("A");
        panel.SetProject(a);
        panel.SetProject(Project("B"));
        panel.SetProject(a);
        Complete(adapter.Requests[0], false, "stale");
        Check(adapter.Requests.Count == 2 && adapter.Requests[1].Id == a.Id.ToString(), "latest A refresh lost");
        Check(Rows(panel).Items.Count == 0, "old A response published after A-B-A");
        Complete(adapter.Requests[1], false, "A");
        Check(Rows(panel).Items.Count == 1, "latest A results missing");
    }

    private static void CurrentErrorAndSelectionReset()
    {
        var adapter = new DelayedAdapter();
        var panel = new GitRepositoriesPanel();
        panel.Initialize(adapter);
        panel.SetProject(Project("A"));
        Complete(adapter.Requests[0], true, "A");
        Check(Summary(panel).Text == "Git status could not be loaded." &&
            Equals(Summary(panel).ToolTip, "stale error"), "current error missing");
        panel.SetProject(null);
        Check(Summary(panel).Text == "Select a project" && Summary(panel).ToolTip is null,
            "previous diagnostic survived selection reset");
    }

    private static void Complete(Request request, bool fail, string name)
    {
        if (fail) request.Completion.SetException(new InvalidOperationException("stale error"));
        else request.Completion.SetResult([Entry(name)]);
    }

    private static ItemsControl Rows(GitRepositoriesPanel panel) => (ItemsControl)panel.FindName("Repositories");
    private static TextBlock Summary(GitRepositoriesPanel panel) => (TextBlock)panel.FindName("GitSummary");
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed record Request(string Id, TaskCompletionSource<IReadOnlyList<GitRepositoryEntry>> Completion);

    private sealed class DelayedAdapter : IGitRepositoryAdapter
    {
        public List<Request> Requests { get; } = [];
        public Task<IReadOnlyList<GitRepositoryEntry>> ListAsync(string projectId)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<GitRepositoryEntry>>();
            Requests.Add(new(projectId, completion));
            return completion.Task;
        }
        public Task<GitOperationResult> SwitchAsync(string projectId, string repositoryPath, GitBranchEntry branch) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitOperationResult>> FetchAsync(string projectId, IReadOnlyList<string> repositoryPaths) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitOperationResult>> PullAsync(string projectId, IReadOnlyList<string> repositoryPaths) => throw new NotSupportedException();
    }
}
