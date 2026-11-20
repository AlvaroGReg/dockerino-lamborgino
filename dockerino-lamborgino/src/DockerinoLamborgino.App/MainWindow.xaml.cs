using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.App;

public partial class MainWindow : Window
{
    private readonly SessionRegistry registry = new();
    private readonly ShellState shellState = new();
    private readonly ObservableCollection<SessionItem> sessions = [];
    private readonly IProjectConfigurationAdapter projectConfigurations;
    private readonly ProjectConfigurationService configurationService;
    private readonly LauncherWindow launcher;
    private bool exiting;

    public MainWindow()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        string? storage = null;
        bool launcherOnly = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--launcher-only" && !launcherOnly) launcherOnly = true;
            else if (args[i] == "--project-storage" && storage is null && i + 1 < args.Length &&
                     System.IO.Path.IsPathFullyQualified(args[i + 1])) storage = args[++i];
            else throw new ArgumentException("Usage: [--launcher-only] [--project-storage <absolute directory>]");
        }
        projectConfigurations = new CoreProjectConfigurationAdapter(storage);
        configurationService = new ProjectConfigurationService(storage);
        InitializeComponent();
        launchOrchestrator = new LaunchOrchestrator(new WindowSessionFactory(this));
        commandServer = new LocalCommandServer(DispatchCommandAsync);
        commandServer.Start();
        shellState.ToggleMenu();
        SessionsList.ItemsSource = sessions;
        RefreshLaunchProjects();
        launcher = new LauncherWindow();
        launcher.OpenRequested += ShowDashboard;
        launcher.ExitRequested += ExitApplication;
        launcher.Show();
        if (launcherOnly) Loaded += (_, _) => Hide();
        Closing += (_, e) =>
        {
            if (!exiting)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            if ((registry.List().Any(s => s.State is SessionState.Starting or SessionState.Running or SessionState.Stopping) ||
                managedViews.Values.Any(v => v.State is TerminalSessionState.Starting or TerminalSessionState.Running) ||
                launchOrchestrator.List().Any(l => l.State == ManagedLaunchState.Running)) &&
                MessageBox.Show(this, "Exiting will terminate running terminal sessions. Continue?",
                    "Exit dockerino-lamborgino", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                exiting = false;
            }
        };
        Closed += (_, _) =>
        {
            foreach (var item in sessions)
                if (managedViews.TryGetValue(item.Id, out var view)) view.Stop();
                else registry.Stop(item.Id);
            _ = commandServer.DisposeAsync();
            launcher.Close();
        };
    }

    private void FindProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select one project directory" };
        if (dialog.ShowDialog(this) == true)
        {
            new ProjectManagerWindow(projectConfigurations, dialog.FolderName) { Owner = this }.ShowDialog();
            RefreshLaunchProjects();
        }
    }

    private void ManageProjects_Click(object sender, RoutedEventArgs e)
    {
        new ProjectManagerWindow(projectConfigurations) { Owner = this }.ShowDialog();
        RefreshLaunchProjects();
    }

    private void RefreshLaunchProjects()
    {
        var selected = (LaunchProjects.SelectedItem as ProjectConfiguration)?.Id;
        var projects = configurationService.List().Where(r => r.Project is not null && r.Issues.Count == 0)
            .Select(r => r.Project!).ToArray();
        LaunchProjects.ItemsSource = projects;
        LaunchProjects.SelectedItem = projects.FirstOrDefault(p => p.Id == selected) ?? projects.FirstOrDefault();
    }

    private void LaunchProjects_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        LaunchActions.ItemsSource = (LaunchProjects.SelectedItem as ProjectConfiguration)?.Actions;
        LaunchActions.SelectedIndex = LaunchActions.Items.Count > 0 ? 0 : -1;
    }

    private void LaunchActions_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var action = LaunchActions.SelectedItem as LaunchAction;
        LaunchDescription.Text = action is null ? "" : $"{action.Name} ({action.Color})\n{action.Description}";
        LaunchButton.IsEnabled = action is not null;
        LaunchButton.Background = action is null ? null :
            (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(action.Color)!;
    }

    private void ManageGit_Click(object sender, RoutedEventArgs e) =>
        new GitWindow(projectConfigurations, new CoreGitWindowAdapter(projectConfigurations)) { Owner = this }.ShowDialog();

    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        var view = new TerminalSessionView { Visibility = Visibility.Collapsed };
        TerminalHost.Children.Add(view);
        Guid? sessionId = null;
        var pending = new List<TerminalSessionState>();
        void Report(TerminalSessionState state)
        {
            if (state is TerminalSessionState.Starting or TerminalSessionState.Stopped) return;
            if (sessionId is null) { pending.Add(state); return; }
            var mapped = state switch
            {
                TerminalSessionState.Running => SessionState.Running,
                TerminalSessionState.Exited => SessionState.Exited,
                _ => SessionState.Failed
            };
            registry.ReportState(sessionId.Value, mapped, mapped == SessionState.Failed ? view.Status : null);
        }
        view.StateChanged += Report;
        var info = registry.Open(null, new ViewSession(view));
        sessionId = info.Id;
        foreach (var state in pending) Report(state);
        var item = new SessionItem(info.Id, $"Terminal {sessions.Count + 1}", view);
        item.Status = view.Status;
        sessions.Add(item);
        view.StatusChanged += status =>
        {
            item.Status = status;
            SessionsList.Items.Refresh();
            if (SessionsList.SelectedItem == item) StatusText.Text = status;
        };
        SessionsList.SelectedItem = item;
    }

    private void SessionsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = SessionsList.SelectedItem as SessionItem;
        foreach (var item in sessions)
            item.View.Visibility = item == selected ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = selected is null ? Visibility.Visible : Visibility.Collapsed;
        if (selected is null) return;
        if (registry.List().Any(s => s.Id == selected.Id))
            registry.Select(selected.Id);
        StatusText.Text = selected.View.Status;
    }

    private async void StopSession_Click(object sender, RoutedEventArgs e)
    {
        if (SessionsList.SelectedItem is not SessionItem item) return;
        if (managedViews.ContainsKey(item.Id))
        {
            try { await launchOrchestrator.StopSessionAsync(item.Id); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
            return;
        }
        var result = registry.Stop(item.Id);
        if (result.Error is not null) StatusText.Text = result.Error;
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        shellState.ToggleMenu();
        MenuPanel.Visibility = shellState.IsMenuVisible ? Visibility.Visible : Visibility.Collapsed;
        MenuColumn.Width = shellState.IsMenuVisible ? new GridLength(260) : new GridLength(0);
        MenuButton.Content = shellState.IsMenuVisible ? "Hide menu" : "Show menu";
    }

    private void ShowDashboard()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        exiting = true;
        Close();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private sealed class ViewSession(TerminalSessionView view) : ISession
    {
        public void Start() => view.Start();
        public void Stop() => view.Stop();
    }

    private sealed class SessionItem(Guid id, string name, TerminalSessionView view)
    {
        public Guid Id { get; } = id;
        public TerminalSessionView View { get; } = view;
        public string Status { get; set; } = "Starting";
        public string Label => $"{name} — {Status}";
    }
}
