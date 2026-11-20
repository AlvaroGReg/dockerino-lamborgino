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
        InitializeComponent();
        shellState.ToggleMenu();
        SessionsList.ItemsSource = sessions;
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
            if (registry.List().Any(s => s.State is SessionState.Starting or SessionState.Running or SessionState.Stopping) &&
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
                registry.Stop(item.Id);
            launcher.Close();
        };
    }

    private void FindProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select one project directory" };
        if (dialog.ShowDialog(this) == true)
            new ProjectManagerWindow(projectConfigurations, dialog.FolderName) { Owner = this }.ShowDialog();
    }

    private void ManageProjects_Click(object sender, RoutedEventArgs e) =>
        new ProjectManagerWindow(projectConfigurations) { Owner = this }.ShowDialog();

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
        registry.Select(selected.Id);
        StatusText.Text = selected.View.Status;
    }

    private void StopSession_Click(object sender, RoutedEventArgs e)
    {
        if (SessionsList.SelectedItem is not SessionItem item) return;
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
