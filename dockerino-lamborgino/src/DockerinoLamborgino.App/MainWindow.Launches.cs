using System.Windows;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.App;

public partial class MainWindow
{
    private readonly LaunchOrchestrator launchOrchestrator;
    private readonly LocalCommandServer commandServer;
    private readonly Dictionary<Guid, TerminalSessionView> managedViews = [];

    private ManagedLaunchInfo StartLaunch(Guid projectId, Guid actionId)
    {
        var loaded = configurationService.Get(projectId);
        if (loaded.Project is null || loaded.Issues.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, loaded.Issues.Select(i => $"{i.Path}: {i.Message}")));
        return launchOrchestrator.Start(loaded.Project, actionId);
    }

    private Task<LocalCommandResponse> DispatchCommandAsync(LocalCommandRequest request, CancellationToken token) =>
        Dispatcher.InvokeAsync(() => HandleCommandAsync(request), System.Windows.Threading.DispatcherPriority.Normal, token).Task.Unwrap();

    private async Task<LocalCommandResponse> HandleCommandAsync(LocalCommandRequest request)
    {
        Guid Required(Guid? id) => id is { } value && value != Guid.Empty
            ? value : throw new ArgumentException("A managed ID is required.");
        object result;
        switch (request.Operation)
        {
            case "launch.start":
                result = StartLaunch(Required(request.ProjectId), Required(request.ActionId));
                break;
            case "launch.list": result = launchOrchestrator.List(); break;
            case "launch.inspect": result = launchOrchestrator.Get(Required(request.Id)); break;
            case "launch.stop":
                await launchOrchestrator.StopLaunchAsync(Required(request.Id));
                result = launchOrchestrator.Get(Required(request.Id));
                break;
            case "session.list":
                result = launchOrchestrator.List().SelectMany(l => l.Processes).Cast<object>()
                    .Concat(registry.List()).ToArray();
                break;
            case "session.inspect":
                var inspectId = Required(request.Id);
                result = registry.List().FirstOrDefault(s => s.Id == inspectId) as object ?? FindManagedSession(inspectId);
                break;
            case "session.stop":
                var stopId = Required(request.Id);
                if (registry.List().Any(s => s.Id == stopId)) result = registry.Stop(stopId);
                else
                {
                    await launchOrchestrator.StopSessionAsync(stopId);
                    result = FindManagedSession(stopId);
                }
                break;
            case "session.output":
                var id = Required(request.Id);
                var manual = registry.List().FirstOrDefault(s => s.Id == id);
                var process = manual is null ? FindManagedSession(id) : null;
                var view = manual is null ? managedViews.GetValueOrDefault(id) : sessions.First(s => s.Id == id).View;
                var text = view?.Output ?? "";
                if (request.Offset < 0 || request.Offset > text.Length) throw new ArgumentOutOfRangeException(nameof(request.Offset));
                result = new LocalSessionOutput(text[(int)request.Offset..], text.Length,
                    view?.State is TerminalSessionState.Exited or TerminalSessionState.Failed or TerminalSessionState.Stopped ||
                    (view is null && process?.State is ManagedProcessState.Failed or ManagedProcessState.Stopped));
                break;
            case "projects.refresh":
                var preferredProjectId = request.ProjectId is { } projectId && projectId != Guid.Empty
                    ? projectId : (Guid?)null;
                RefreshProjects(preferredProjectId);
                var selectedProjectId = (ProjectsList.SelectedItem as ProjectConfiguration)?.Id;
                if (preferredProjectId is not null && selectedProjectId != preferredProjectId)
                    throw new KeyNotFoundException($"Project not found after refreshing: {preferredProjectId}");
                result = new { refreshed = true, applicationRunning = true, projectCount = projects.Count, selectedProjectId };
                break;
            case "dashboard.open":
                ShowDashboard();
                result = new { opened = true };
                break;
            default: throw new ArgumentException("Unknown operation.");
        }
        return new(true, result);
    }

    private ManagedProcessInfo FindManagedSession(Guid id) =>
        launchOrchestrator.List().SelectMany(l => l.Processes).FirstOrDefault(p => p.SessionId == id)
        ?? throw new KeyNotFoundException($"Unknown session ID: {id}");

    private sealed class WindowSessionFactory(MainWindow window) : ILaunchSessionFactory
    {
        public async Task<ILaunchSession> CreateAsync(LaunchSessionRequest request, CancellationToken cancellationToken) =>
            await window.Dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var view = new TerminalSessionView { Visibility = Visibility.Collapsed };
                window.managedViews.Add(request.SessionId, view);
                window.TerminalHost.Children.Add(view);
                var item = new SessionItem(request.SessionId, request.ProcessName, view);
                window.sessions.Add(item);
                view.StatusChanged += status =>
                {
                    item.Status = status;
                    window.SessionsList.Items.Refresh();
                    if (window.SessionsList.SelectedItem == item) window.StatusText.Text = status;
                };
                window.SessionsList.SelectedItem = item;
                window.ShowTerminalPanel();
                return new WindowLaunchSession(view, request.WorkingDirectory);
            }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken);
    }

    private sealed class WindowLaunchSession(TerminalSessionView view, string directory) : ILaunchSession
    {
        public async Task<IReadOnlyList<LaunchCommandResult>> RunSequenceAsync(IReadOnlyList<string> commands,
            Action<LaunchCommandResult> commandCompleted, CancellationToken cancellationToken)
        {
            Task<IReadOnlyList<int>> task = await view.Dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                view.ConfigureCommands(commands, directory);
                view.CommandCompleted += (index, code) => commandCompleted(new(index, code));
                view.Start();
                return view.CommandResults!;
            });
            var results = await task.WaitAsync(cancellationToken);
            return results.Select((code, index) => new LaunchCommandResult(index, code)).ToArray();
        }

        public async Task StopAsync() => await view.Dispatcher.InvokeAsync(view.Stop);
    }
}
