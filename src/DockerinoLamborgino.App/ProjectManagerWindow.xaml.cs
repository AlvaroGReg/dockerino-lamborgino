using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.App;

public partial class ProjectManagerWindow : Window
{
    private readonly IProjectConfigurationAdapter adapter;
    private string? projectId;
    private string? revision;
    private List<LaunchAction> actions = [];
    private List<string> repositories = [];

    public ProjectManagerWindow(IProjectConfigurationAdapter adapter, string? directory = null)
    {
        this.adapter = adapter;
        InitializeComponent();
        RefreshProjects();
        DirectoryBox.Text = directory ?? "";
        RefreshEditor();
    }

    private void RefreshProjects()
    {
        try
        {
            var result = adapter.List();
            ProjectsList.ItemsSource = result.Projects;
            ErrorsBox.Text = string.Join(Environment.NewLine, result.Errors);
        }
        catch (Exception ex) { ErrorsBox.Text = ex.Message; }
    }

    private void RefreshEditor()
    {
        RepositoriesList.ItemsSource = null;
        RepositoriesList.ItemsSource = repositories;
        ActionsList.ItemsSource = null;
        ActionsList.ItemsSource = actions;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshProjects();

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select one project directory" };
        if (dialog.ShowDialog(this) != true) return;
        projectId = null;
        revision = null;
        NameBox.Clear();
        DirectoryBox.Text = dialog.FolderName;
        actions = [];
        repositories = [];
        ProjectsList.SelectedItem = null;
        RefreshEditor();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select one project directory" };
        if (dialog.ShowDialog(this) == true) DirectoryBox.Text = dialog.FolderName;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectConfigurationEntry entry) return;
        try { LoadDraft(adapter.Load(entry.Id)); }
        catch (Exception ex) { ErrorsBox.Text = ex.Message; }
    }

    private void LoadDraft(ProjectConfigurationDraft draft)
    {
        var loadedActions = JsonSerializer.Deserialize<List<LaunchAction>>(draft.ConfigurationJson, ProjectConfigurationService.JsonOptions)
            ?? throw new InvalidOperationException("The project has no action list.");
        var loadedRepositories = JsonSerializer.Deserialize<List<string>>(draft.RepositoriesJson, ProjectConfigurationService.JsonOptions)
            ?? throw new InvalidOperationException("The project has no repository list.");
        projectId = draft.Id;
        revision = draft.Revision;
        NameBox.Text = draft.Name;
        DirectoryBox.Text = draft.Directory;
        actions = loadedActions;
        repositories = loadedRepositories;
        RefreshEditor();
    }

    private void AddRepository_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select a repository directory" };
        if (dialog.ShowDialog(this) != true) return;
        repositories.Add(dialog.FolderName);
        RepositoriesList.Items.Refresh();
        RepositoriesList.SelectedIndex = repositories.Count - 1;
    }

    private void RemoveRepository_Click(object sender, RoutedEventArgs e)
    {
        if (RepositoriesList.SelectedIndex < 0) return;
        repositories.RemoveAt(RepositoriesList.SelectedIndex);
        RepositoriesList.Items.Refresh();
    }

    private void ActionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != ActionsList) return;
        var action = ActionsList.SelectedItem as LaunchAction;
        ActionEditor.DataContext = action;
        ActionEditor.IsEnabled = action is not null;
        ProcessesList.ItemsSource = action?.Processes;
        ProcessesList.SelectedIndex = -1;
        ProcessEditor.DataContext = null;
        ProcessEditor.IsEnabled = false;
        StepsList.ItemsSource = null;
        StepEditor.DataContext = null;
        StepEditor.IsEnabled = false;
    }

    private void ProcessesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != ProcessesList) return;
        var process = ProcessesList.SelectedItem as LaunchProcess;
        ProcessEditor.DataContext = process;
        ProcessEditor.IsEnabled = process is not null;
        StepsList.ItemsSource = process?.Steps;
        StepsList.SelectedIndex = -1;
        StepEditor.DataContext = null;
        StepEditor.IsEnabled = false;
    }

    private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != StepsList) return;
        var step = StepsList.SelectedItem as LaunchStep;
        StepEditor.DataContext = step;
        StepEditor.IsEnabled = step is not null;
    }

    private void AddAction_Click(object sender, RoutedEventArgs e)
    {
        var action = new LaunchAction { Id = Guid.NewGuid(), Name = "New action", Color = "#008080" };
        actions.Add(action);
        ActionsList.Items.Refresh();
        ActionsList.SelectedItem = action;
    }

    private void RemoveAction_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsList.SelectedItem is not LaunchAction action) return;
        actions.Remove(action);
        ActionsList.Items.Refresh();
    }

    private void AddProcess_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsList.SelectedItem is not LaunchAction action) return;
        var process = new LaunchProcess { Id = Guid.NewGuid(), Name = "New process", WorkingDirectory = DirectoryBox.Text };
        action.Processes.Add(process);
        ProcessesList.Items.Refresh();
        ProcessesList.SelectedItem = process;
    }

    private void RemoveProcess_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsList.SelectedItem is not LaunchAction action || ProcessesList.SelectedItem is not LaunchProcess process) return;
        action.Processes.Remove(process);
        ProcessesList.Items.Refresh();
    }

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select the process working directory" };
        if (dialog.ShowDialog(this) == true && ProcessesList.SelectedItem is LaunchProcess process)
        {
            process.WorkingDirectory = dialog.FolderName;
            WorkingDirectoryBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        }
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessesList.SelectedItem is not LaunchProcess process) return;
        var step = new LaunchStep { Id = Guid.NewGuid() };
        process.Steps.Add(step);
        StepsList.Items.Refresh();
        StepsList.SelectedItem = step;
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessesList.SelectedItem is not LaunchProcess process || StepsList.SelectedItem is not LaunchStep step) return;
        // Legacy explicit dependencies are redundant with sequential fail-fast
        // execution. Remove references to a step the user has deleted.
        foreach (var dependent in process.Steps.Where(s => s.DependsOnSuccessOf == step.Id))
            dependent.DependsOnSuccessOf = null;
        process.Steps.Remove(step);
        StepsList.Items.Refresh();
    }

    private void StepUp_Click(object sender, RoutedEventArgs e) => MoveStep(-1);
    private void StepDown_Click(object sender, RoutedEventArgs e) => MoveStep(1);

    private void MoveStep(int direction)
    {
        if (ProcessesList.SelectedItem is not LaunchProcess process || StepsList.SelectedItem is not LaunchStep step) return;
        int index = process.Steps.IndexOf(step), target = index + direction;
        if (target < 0 || target >= process.Steps.Count) return;
        (process.Steps[index], process.Steps[target]) = (process.Steps[target], process.Steps[index]);
        var previous = new HashSet<Guid>();
        foreach (var current in process.Steps)
        {
            if (current.DependsOnSuccessOf is Guid id && !previous.Contains(id))
                current.DependsOnSuccessOf = null;
            previous.Add(current.Id);
        }
        StepsList.Items.Refresh();
        StepsList.SelectedItem = step;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var draft = new ProjectConfigurationDraft(projectId, revision, NameBox.Text, DirectoryBox.Text,
                JsonSerializer.Serialize(actions, ProjectConfigurationService.JsonOptions),
                JsonSerializer.Serialize(repositories, ProjectConfigurationService.JsonOptions));
            var summary = new StringBuilder().AppendLine($"Name: {draft.Name}").AppendLine($"Directory: {draft.Directory}")
                .AppendLine().AppendLine("Repositories:");
            foreach (var repository in repositories) summary.AppendLine($"  {repository}");
            summary.AppendLine().AppendLine("Actions:");
            foreach (var action in actions)
            {
                summary.AppendLine($"  {action.Name} ({action.Color}) — {action.Description}");
                foreach (var process in action.Processes)
                {
                    summary.AppendLine($"    {process.Name} — {process.WorkingDirectory}");
                    foreach (var step in process.Steps) summary.AppendLine($"      {step.Command}");
                }
            }
            var review = new Window { Owner = this, Title = "Review before saving", Width = 650, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new Thickness(10) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var confirm = new Button { Content = "Save", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 5, 12, 5), IsCancel = true };
            confirm.Click += (_, _) => review.DialogResult = true;
            buttons.Children.Add(confirm); buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
            panel.Children.Add(new TextBox { Text = summary.ToString(), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            review.Content = panel;
            if (review.ShowDialog() != true) return;
            var result = adapter.Save(draft);
            if (!result.Saved) { ErrorsBox.Text = string.Join(Environment.NewLine, result.Errors); return; }
            revision = null;
            projectId = result.Id;
            LoadDraft(adapter.Load(result.Id!));
            RefreshProjects();
            if (string.IsNullOrEmpty(ErrorsBox.Text)) ErrorsBox.Text = "Configuration saved.";
        }
        catch (Exception ex) { ErrorsBox.Text = ex.Message; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
