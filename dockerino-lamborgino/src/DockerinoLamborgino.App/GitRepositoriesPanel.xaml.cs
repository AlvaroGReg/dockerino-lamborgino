using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.App;

public partial class GitRepositoriesPanel : UserControl
{
    private readonly ObservableCollection<RepositoryRow> repositories = [];
    private IGitRepositoryAdapter? git;
    private ProjectConfiguration? project;
    private bool busy;

    public GitRepositoriesPanel()
    {
        InitializeComponent();
        Repositories.ItemsSource = repositories;
    }

    public void Initialize(IGitRepositoryAdapter adapter) => git = adapter;

    public void SetProject(ProjectConfiguration? selectedProject)
    {
        project = selectedProject;
        repositories.Clear();
        OperationMessages.Clear();
        NoRepositoriesText.Visibility = project?.Repositories?.Count is > 0 ? Visibility.Collapsed : Visibility.Visible;
        GitSummary.Text = project is null ? "Select a project" : $"{project.Repositories?.Count ?? 0} repositories";

        if (project is null)
        {
            NoRepositoriesText.Visibility = Visibility.Collapsed;
            return;
        }

        _ = RefreshRepositoriesAsync();
    }

    private readonly Dictionary<string, string> OperationMessages = new(StringComparer.OrdinalIgnoreCase);

    private async void GitExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (repositories.Count == 0 && project is not null) await RefreshRepositoriesAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshRepositoriesAsync();

    private async Task RefreshRepositoriesAsync()
    {
        if (busy || project is null || git is null) return;

        busy = true;
        RefreshButton.IsEnabled = false;
        GitSummary.Text = "Refreshing Git status…";
        try
        {
            var entries = await git.ListAsync(project.Id.ToString());
            repositories.Clear();
            foreach (var entry in entries)
            {
                var row = new RepositoryRow(entry, GetDisplayName(entry.Path), OperationMessages.GetValueOrDefault(entry.Path));
                repositories.Add(row);
            }

            NoRepositoriesText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GitSummary.Text = $"{entries.Count} repositories";
            GitSummary.ToolTip = null;
        }
        catch (Exception ex)
        {
            GitSummary.Text = "Git status could not be loaded.";
            GitSummary.ToolTip = ex.Message;
            NoRepositoriesText.Visibility = Visibility.Collapsed;
        }
        finally
        {
            busy = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private string GetDisplayName(string path)
    {
        if (string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(project?.Directory ?? ""),
                StringComparison.OrdinalIgnoreCase))
            return $"{project?.Name} (project)";
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
    }

    private async void Branch_DropDownClosed(object? sender, EventArgs e)
    {
        if (busy || sender is not ComboBox comboBox ||
            comboBox.DataContext is not RepositoryRow row ||
            comboBox.SelectedItem is not GitBranchChoice { IsBranch: true } branch ||
            project is null || git is null)
            return;

        if (!branch.IsRemote && string.Equals(branch.Name, row.Entry.CurrentBranch, StringComparison.Ordinal))
            return;

        await RunRepositoryOperationAsync(row, async () =>
        {
            var result = await git.SwitchAsync(project.Id.ToString(), row.Entry.Path, new(branch.Name, branch.IsRemote));
            SetOperationResult(row, result);
        });
    }

    private async void Fetch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RepositoryRow row || project is null || git is null) return;
        await RunRepositoryOperationAsync(row, async () =>
        {
            var results = await git.FetchAsync(project.Id.ToString(), [row.Entry.Path]);
            SetOperationResult(row, results[0]);
        });
    }

    private async void Pull_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RepositoryRow row || project is null || git is null) return;
        await RunRepositoryOperationAsync(row, async () =>
        {
            var results = await git.PullAsync(project.Id.ToString(), [row.Entry.Path]);
            SetOperationResult(row, results[0]);
        });
    }

    private async Task RunRepositoryOperationAsync(RepositoryRow row, Func<Task> operation)
    {
        if (busy) return;
        busy = true;
        RefreshButton.IsEnabled = false;
        foreach (var repository in repositories) repository.IsBusy = true;
        var projectId = project?.Id.ToString();
        GitSummary.Text = $"Working on {row.Name}…";
        try
        {
            await operation();
            if (project?.Id.ToString() == projectId)
                OperationMessages[row.Entry.Path] = row.OperationMessage;
        }
        catch (Exception ex)
        {
            row.OperationMessage = $"ERROR — {ex.Message}";
            if (project?.Id.ToString() == projectId)
                OperationMessages[row.Entry.Path] = row.OperationMessage;
        }
        finally
        {
            foreach (var repository in repositories) repository.IsBusy = false;
            busy = false;
            RefreshButton.IsEnabled = true;
            await RefreshRepositoriesAsync();
        }
    }

    private static void SetOperationResult(RepositoryRow row, GitOperationResult result) =>
        row.OperationMessage = $"{(result.Succeeded ? "OK" : "ERROR")} — {result.Message}";

    private sealed record GitBranchChoice(string Label, string Name = "", bool IsRemote = false,
        bool IsHeader = false, bool IsSeparator = false)
    {
        public bool IsBranch => !IsHeader && !IsSeparator;
    }

    private sealed class RepositoryRow : INotifyPropertyChanged
    {
        private bool isBusy;
        private string operationMessage;
        private GitBranchChoice? selectedBranch;

        public RepositoryRow(GitRepositoryEntry entry, string name, string? message)
        {
            Entry = entry;
            Name = name;
            operationMessage = message ?? "";
            BranchChoices = [new("LOCAL BRANCHES", IsHeader: true)];
            foreach (var branch in entry.LocalBranches)
                BranchChoices.Add(new($"● {branch.Name}", branch.Name));
            BranchChoices.Add(new("", IsSeparator: true));
            BranchChoices.Add(new("REMOTE BRANCHES", IsHeader: true));
            foreach (var branch in entry.RemoteBranches)
                BranchChoices.Add(new($"◆ {branch.Name}", branch.Name, true));
            selectedBranch = BranchChoices.FirstOrDefault(branch => branch.IsBranch &&
                !branch.IsRemote && string.Equals(branch.Name, entry.CurrentBranch, StringComparison.Ordinal));
        }

        public GitRepositoryEntry Entry { get; }
        public string Name { get; }
        public string Path => Entry.Path;
        public string BranchSummary => $"Current branch: {Entry.CurrentBranch}";
        public string Status => Entry.Error ?? Entry.Status;
        public ObservableCollection<GitBranchChoice> BranchChoices { get; }
        public bool IsBusy { get => isBusy; set { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanOperate)); OnPropertyChanged(nameof(CanSwitch)); } }
        public bool CanOperate => !IsBusy && Entry.Error is null;
        public bool CanSwitch => CanOperate && BranchChoices.Any(branch => branch.IsBranch);
        public string OperationMessage { get => operationMessage; set { operationMessage = value; OnPropertyChanged(); } }
        public GitBranchChoice? SelectedBranch { get => selectedBranch; set { selectedBranch = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
