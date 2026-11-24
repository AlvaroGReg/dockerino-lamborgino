using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.App;

public partial class MainWindow : Window
{
    private const double ProjectColumnWidth = 258;
    private const double ToolsColumnWidth = 310;
    private const double TerminalColumnWidth = 600;
    private readonly SessionRegistry registry = new();
    private readonly ObservableCollection<SessionItem> sessions = [];
    private readonly ObservableCollection<ProjectConfiguration> projects = [];
    private readonly IProjectConfigurationAdapter projectConfigurations;
    private readonly ProjectConfigurationService configurationService;
    private readonly DisplayOrderStore displayOrder;
    private readonly ObservableCollection<LaunchAction> displayedActions = [];
    private Point dragStart;
    private object? dragItem;
    private readonly LauncherWindow launcher;
    private bool exiting;
    private int resizeVersion;

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
        displayOrder = new DisplayOrderStore(configurationService.StorageDirectory);
        InitializeComponent();
        GitRepositoriesPanel.Initialize(new CoreGitRepositoryAdapter(projectConfigurations));
        launchOrchestrator = new LaunchOrchestrator(new WindowSessionFactory(this));
        commandServer = new LocalCommandServer(DispatchCommandAsync);
        commandServer.Start();
        SessionsList.ItemsSource = sessions;
        ProjectsList.ItemsSource = projects;
        RefreshProjects();
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
            new ProjectManagerWindow(projectConfigurations, displayOrder, directory: dialog.FolderName) { Owner = this }.ShowDialog();
            RefreshProjects();
        }
    }

    private void ManageProjects_Click(object sender, RoutedEventArgs e)
    {
        var selectedProjectId = (ProjectsList.SelectedItem as ProjectConfiguration)?.Id;
        new ProjectManagerWindow(projectConfigurations, displayOrder, projectId: selectedProjectId) { Owner = this }.ShowDialog();
        RefreshProjects((ProjectsList.SelectedItem as ProjectConfiguration)?.Id);
    }

    private void RefreshProjects(Guid? preferredId = null)
    {
        var selectedId = preferredId ?? (ProjectsList.SelectedItem as ProjectConfiguration)?.Id;
        var loaded = configurationService.List().Where(r => r.Project is not null && r.Issues.Count == 0)
            .Select(r => r.Project!).ToArray();
        projects.Clear();
        IReadOnlyList<ProjectConfiguration> ordered = loaded;
        try { ordered = DisplayOrderStore.Apply(loaded, displayOrder.Read(), p => p.Id); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { StatusText.Text = $"Could not load project order: {ex.Message}"; }
        foreach (var project in ordered) projects.Add(project);
        ProjectsList.SelectedItem = projects.FirstOrDefault(p => p.Id == selectedId);
        UpdateProjectDetails();
    }

    private void ProjectsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateProjectDetails();
    }

    private void ProjectsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBlock { Tag: ProjectConfiguration }) return;
        if (e.OriginalSource is DependencyObject quickLaunchSource &&
            System.Windows.Controls.ItemsControl.ContainerFromElement(ProjectsList, quickLaunchSource) is System.Windows.Controls.ListBoxItem &&
            FindAncestor<System.Windows.Controls.Button>(quickLaunchSource) is { DataContext: LaunchAction }) return;
        if (e.OriginalSource is not DependencyObject source ||
            System.Windows.Controls.ItemsControl.ContainerFromElement(ProjectsList, source) is not System.Windows.Controls.ListBoxItem item ||
            !item.IsSelected) return;

        ProjectsList.SelectedItem = null;
        e.Handled = true;
    }

    private void UpdateProjectDetails()
    {
        var project = ProjectsList.SelectedItem as ProjectConfiguration;
        SelectedProjectName.Text = project?.Name ?? "Select a project";
        SelectedProjectPath.Text = project?.Directory ?? "";
        EditProjectButton.IsEnabled = project is not null;
        GitRepositoriesPanel.SetProject(project);
        displayedActions.Clear();
        if (project is not null)
        {
            IReadOnlyList<LaunchAction> ordered = project.Actions;
            try { ordered = DisplayOrderStore.Apply(project.Actions, displayOrder.Read(project.Id), a => a.Id); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { StatusText.Text = $"Could not load launch order: {ex.Message}"; }
            foreach (var action in ordered) displayedActions.Add(action);
        }
        LaunchActionsList.ItemsSource = displayedActions;
        NoActionsText.Visibility = project is null || project.Actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LaunchDescription.Text = project is null ? "" : $"{project.Actions.Count} launch action(s) configured.";
        if (project is null) StatusText.Text = "Choose a project to see its tools.";
        UpdateColumns();
    }

    private void UpdateStatusIndicators()
    {
        bool HasError(IEnumerable<SessionItem> items) =>
            items.Any(item => item.State == TerminalSessionState.Failed || item.LaunchError is not null);
        bool IsLoading(IEnumerable<SessionItem> items) =>
            items.Any(item => item.State == TerminalSessionState.Starting);

        foreach (var item in sessions)
        {
            var container = SessionsList.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.Controls.ListBoxItem;
            if (container is null || FindVisualChild(container, "SessionStatusIndicator") is not { } indicator)
                continue;
            SetIndicator(indicator,
                item.State == TerminalSessionState.Failed || item.LaunchError is not null,
                item.State == TerminalSessionState.Starting);
        }

        foreach (var project in projects)
        {
            var container = ProjectsList.ItemContainerGenerator.ContainerFromItem(project) as System.Windows.Controls.ListBoxItem;
            if (container is null || FindVisualChild(container, "ProjectStatusIndicator") is not { } indicator)
                continue;
            var associated = sessions.Where(item => item.ProjectId == project.Id);
            SetIndicator(indicator, HasError(associated), IsLoading(associated));
        }

        launcher.SetTerminalStatus(HasError(sessions), IsLoading(sessions));
    }

    private static System.Windows.Controls.TextBlock? FindVisualChild(DependencyObject parent, string name)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.TextBlock textBlock && textBlock.Name == name)
                return textBlock;
            if (FindVisualChild(child, name) is { } nested)
                return nested;
        }
        return null;
    }

    private static void SetIndicator(System.Windows.Controls.TextBlock indicator, bool hasError, bool isLoading)
    {
        if (!hasError && !isLoading)
        {
            indicator.Visibility = Visibility.Collapsed;
            indicator.ToolTip = null;
            System.Windows.Automation.AutomationProperties.SetName(indicator, "Terminal status: normal");
            return;
        }

        var color = hasError ? "#FFFF7777" : "#FFFFD166";
        var label = hasError ? "Error" : "Loading";
        indicator.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
            .ConvertFromString(color)!;
        indicator.ToolTip = $"Terminal status: {label.ToLowerInvariant()}";
        indicator.Visibility = Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(indicator, $"Terminal status: {label}");
    }

    private void RefreshStatusIndicators()
    {
        SessionsList.Items.Refresh();
        ProjectsList.Items.Refresh();
        UpdateStatusIndicators();
    }

    private void StatusIndicator_Loaded(object sender, RoutedEventArgs e) => UpdateStatusIndicators();

    private void OrderHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: not null } handle) return;
        dragStart = e.GetPosition(this);
        dragItem = handle.Tag;
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void OrderHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement handle || dragItem is null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            dragItem = null;
            handle.ReleaseMouseCapture();
            return;
        }
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = dragItem;
        dragItem = null;
        handle.ReleaseMouseCapture();
        DragDrop.DoDragDrop(handle, item, DragDropEffects.Move);
    }

    private void OrderHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        dragItem = null;
        if (sender is FrameworkElement handle) handle.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Order_DragOver(object sender, DragEventArgs e)
    {
        bool valid = sender == ProjectsList && e.Data.GetDataPresent(typeof(ProjectConfiguration)) ||
            sender == LaunchActionsList && e.Data.GetDataPresent(typeof(LaunchAction));
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private static int DropIndex(System.Windows.Controls.ItemsControl list, DragEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            System.Windows.Controls.ItemsControl.ContainerFromElement(list, source) is not FrameworkElement container)
            return list.Items.Count;
        var index = list.ItemContainerGenerator.IndexFromContainer(container);
        return index < 0 ? list.Items.Count : index + (e.GetPosition(container).Y >= container.ActualHeight / 2 ? 1 : 0);
    }

    private void Projects_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ProjectConfiguration)) is not ProjectConfiguration item) return;
        var reordered = projects.ToList();
        if (!DisplayOrderStore.Move(reordered, item, DropIndex(ProjectsList, e))) return;
        try
        {
            displayOrder.Save(reordered.Select(p => p.Id).ToArray());
            projects.Move(projects.IndexOf(item), reordered.IndexOf(item));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { StatusText.Text = $"Could not save project order: {ex.Message}"; }
        e.Handled = true;
    }

    private void Actions_Drop(object sender, DragEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectConfiguration project ||
            e.Data.GetData(typeof(LaunchAction)) is not LaunchAction item) return;
        var reordered = displayedActions.ToList();
        if (!DisplayOrderStore.Move(reordered, item, DropIndex(LaunchActionsList, e))) return;
        try
        {
            displayOrder.Save(reordered.Select(a => a.Id).ToArray(), project.Id);
            displayedActions.Move(displayedActions.IndexOf(item), reordered.IndexOf(item));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { StatusText.Text = $"Could not save launch order: {ex.Message}"; }
        e.Handled = true;
    }

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
        item.ProjectId = null;
        item.Status = view.Status;
        item.State = view.State;
        sessions.Add(item);
        view.StateChanged += state =>
        {
            item.State = state;
            RefreshStatusIndicators();
        };
        view.StatusChanged += status =>
        {
            item.Status = status;
            RefreshStatusIndicators();
            if (SessionsList.SelectedItem == item) StatusText.Text = status;
        };
        SessionsList.SelectedItem = item;
        UpdateStatusIndicators();
        ShowTerminalPanel();
    }

    private void SessionsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = SessionsList.SelectedItem as SessionItem;
        foreach (var item in sessions)
            item.View.Visibility = item == selected ? Visibility.Visible : Visibility.Collapsed;
        if (selected is null) return;
        if (registry.List().Any(s => s.Id == selected.Id))
            registry.Select(selected.Id);
        StatusText.Text = selected.View.Status;
    }

    private void RunAction_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectConfiguration project ||
            (sender as System.Windows.Controls.Button)?.Tag is not LaunchAction action) return;
        try
        {
            var launch = StartLaunch(project.Id, action.Id);
            StatusText.Text = $"Launch {action.Name} started.";
            ShowTerminalPanel();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void QuickLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: LaunchAction action } button ||
            FindAncestor<System.Windows.Controls.ListBoxItem>(button) is not { DataContext: ProjectConfiguration project }) return;
        try
        {
            StartLaunch(project.Id, action.Id);
            StatusText.Text = $"Launch {action.Name} started.";
            ShowTerminalPanel();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var current = element; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is T ancestor) return ancestor;
        return null;
    }

    private void LaunchActionButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not LaunchAction action) return;
        try
        {
            button.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
                .ConvertFromString(action.Color)!;
        }
        catch (FormatException) { button.Background = System.Windows.Media.Brushes.DarkSlateGray; }
    }

    private void QuickLaunchButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not LaunchAction action) return;
        try
        {
            button.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
                .ConvertFromString(action.Color)!;
        }
        catch (FormatException) { button.Foreground = System.Windows.Media.Brushes.DarkSlateGray; }
    }

    private async void CloseSessionTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.Tag is not SessionItem item) return;
        var wasSelected = SessionsList.SelectedItem == item;
        if (managedViews.ContainsKey(item.Id))
        {
            try { await launchOrchestrator.StopSessionAsync(item.Id); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
            managedViews.Remove(item.Id);
        }
        else
        {
            var result = registry.Stop(item.Id);
            if (result.Error is not null) StatusText.Text = result.Error;
        }
        sessions.Remove(item);
        TerminalHost.Children.Remove(item.View);
        RefreshStatusIndicators();
        if (wasSelected) SessionsList.SelectedItem = sessions.LastOrDefault();
        UpdateTerminalPanel();
    }

    private void UpdateTerminalPanel()
    {
        UpdateColumns();
    }

    private void UpdateColumns()
    {
        var hasSessions = sessions.Count > 0;
        var hasProject = ProjectsList.SelectedItem is ProjectConfiguration;
        ProjectToolsColumn.Width = hasProject ? new GridLength(ToolsColumnWidth) : new GridLength(0);
        TerminalColumn.Width = hasSessions ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        TerminalPanel.Visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
        var targetWidth = ProjectColumnWidth + (hasProject ? ToolsColumnWidth : 0) +
                          (hasSessions ? TerminalColumnWidth : 0) + 2;
        ResizeWindow(targetWidth);
    }

    private void ShowTerminalPanel() => UpdateColumns();

    private void ResizeWindow(double targetWidth)
    {
        targetWidth = Math.Min(targetWidth, SystemParameters.WorkArea.Width);
        var currentWidth = ActualWidth > 0 ? ActualWidth : Width;
        if (Math.Abs(currentWidth - targetWidth) < 1) return;

        BeginAnimation(WidthProperty, null);
        Width = currentWidth;
        if (IsLoaded)
        {
            var workArea = SystemParameters.WorkArea;
            if (Left < workArea.Left) Left = workArea.Left;
            if (Left + targetWidth > workArea.Right)
                Left = Math.Max(workArea.Left, workArea.Right - targetWidth);
        }

        var version = ++resizeVersion;
        var animation = new DoubleAnimation(currentWidth, targetWidth, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            if (version != resizeVersion) return;
            BeginAnimation(WidthProperty, null);
            Width = targetWidth;
        };
        BeginAnimation(WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
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
    private void ApplicationOptions_Click(object sender, RoutedEventArgs e) =>
        new ApplicationOptionsWindow { Owner = this }.ShowDialog();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindAncestor<System.Windows.Controls.Button>(source) is not null) return;
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
        public string Name { get; } = name;
        public string Status { get; set; } = "Starting";
        public Guid? ProjectId { get; set; }
        public TerminalSessionState? State { get; set; }
        public string? LaunchError { get; set; }
    }
}
