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
    private double expandedWidth;
    private double expandedHeight;

    public MainWindow()
    {
        InitializeComponent();
        shellState.ToggleMenu();
        SessionsList.ItemsSource = sessions;
        Closed += (_, _) =>
        {
            foreach (var item in sessions)
                registry.Stop(item.Id);
        };
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
        MenuColumn.Width = shellState.IsMenuVisible ? new GridLength(240) : new GridLength(0);
        MenuButton.Content = shellState.IsMenuVisible ? "Hide menu" : "Show menu";
    }

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        shellState.ToggleCollapsed();
        if (shellState.IsCollapsed)
        {
            expandedWidth = Width;
            expandedHeight = Height;
            ExpandedContent.Visibility = Visibility.Collapsed;
            MinWidth = 0;
            MinHeight = 0;
            Width = 240;
            Height = 46;
            CollapseButton.Content = "Expand";
        }
        else
        {
            MinWidth = 400;
            MinHeight = 220;
            Width = expandedWidth;
            Height = expandedHeight;
            ExpandedContent.Visibility = Visibility.Visible;
            CollapseButton.Content = "Collapse";
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

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
