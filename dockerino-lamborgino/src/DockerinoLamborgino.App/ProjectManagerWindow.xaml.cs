using System.Text.Json;
using System.Windows;

namespace DockerinoLamborgino.App;

public partial class ProjectManagerWindow : Window
{
    private readonly IProjectConfigurationAdapter adapter;
    private string? projectId;
    private string? revision;

    public ProjectManagerWindow(IProjectConfigurationAdapter adapter, string? directory = null)
    {
        this.adapter = adapter;
        InitializeComponent();
        RefreshProjects();
        DirectoryBox.Text = directory ?? "";
        ConfigurationBox.Text = "[]";
        RepositoriesBox.Text = "[]";
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

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshProjects();

    private void ProjectsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        ActionsList.ItemsSource = (ProjectsList.SelectedItem as ProjectConfigurationEntry)?.Actions;

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select one project directory" };
        if (dialog.ShowDialog(this) != true) return;
        projectId = null;
        revision = null;
        NameBox.Clear();
        DirectoryBox.Text = dialog.FolderName;
        ConfigurationBox.Text = "[]";
        RepositoriesBox.Text = "[]";
        ProjectsList.SelectedItem = null;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select one project directory" };
        if (dialog.ShowDialog(this) == true) DirectoryBox.Text = dialog.FolderName;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectConfigurationEntry entry) return;
        try
        {
            var draft = adapter.Load(entry.Id);
            projectId = draft.Id;
            revision = draft.Revision;
            NameBox.Text = draft.Name;
            DirectoryBox.Text = draft.Directory;
            ConfigurationBox.Text = draft.ConfigurationJson;
            RepositoriesBox.Text = draft.RepositoriesJson;
        }
        catch (Exception ex) { ErrorsBox.Text = ex.Message; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var draft = new ProjectConfigurationDraft(projectId, revision, NameBox.Text, DirectoryBox.Text, ConfigurationBox.Text, RepositoriesBox.Text);
        try
        {
            using var json = JsonDocument.Parse(draft.ConfigurationJson);
            using var repositories = JsonDocument.Parse(draft.RepositoriesJson);
            var review = new Window
            {
                Owner = this, Title = "Review before saving", Width = 650, Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new System.Windows.Controls.DockPanel { Margin = new Thickness(10) };
            var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var confirm = new System.Windows.Controls.Button { Content = "Save", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", Padding = new Thickness(12, 5, 12, 5), IsCancel = true };
            confirm.Click += (_, _) => review.DialogResult = true;
            buttons.Children.Add(confirm);
            buttons.Children.Add(cancel);
            System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom);
            panel.Children.Add(buttons);
            panel.Children.Add(new System.Windows.Controls.TextBox
            {
                Text = $"Name: {draft.Name}\nDirectory: {draft.Directory}\n\nRepositories:\n{draft.RepositoriesJson}\n\nActions:\n{draft.ConfigurationJson}",
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
            });
            review.Content = panel;
            if (review.ShowDialog() != true) return;
            var result = adapter.Save(draft);
            if (!result.Saved) { ErrorsBox.Text = string.Join(Environment.NewLine, result.Errors); return; }
            projectId = result.Id;
            revision = null;
            // Reload the revision and the corresponding data together; never silently
            // reuse a revision with stale editor contents after a concurrent update.
            var saved = adapter.Load(result.Id!);
            projectId = saved.Id;
            revision = saved.Revision;
            NameBox.Text = saved.Name;
            DirectoryBox.Text = saved.Directory;
            ConfigurationBox.Text = saved.ConfigurationJson;
            RepositoriesBox.Text = saved.RepositoriesJson;
            RefreshProjects();
            if (string.IsNullOrEmpty(ErrorsBox.Text)) ErrorsBox.Text = "Configuration saved.";
        }
        catch (Exception ex) { ErrorsBox.Text = ex.Message; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
