namespace DockerinoLamborgino.Core;

/// <summary>
/// Creates one interactive, owned terminal per configured process. Implementations must
/// honor cancellation during creation, and clean up any partially created resources.
/// </summary>
public interface ILaunchSessionFactory
{
    Task<ILaunchSession> CreateAsync(LaunchSessionRequest request, CancellationToken cancellationToken);
}

public sealed record LaunchSessionRequest(Guid LaunchId, Guid SessionId, Guid ProjectId,
    Guid ActionId, Guid ProcessId, string ProcessName, string WorkingDirectory);

/// <summary>
/// Runs the complete sequence in one interactive terminal, in order, stopping on the
/// first nonzero exit. Report completions in order and return the completed prefix as
/// final results (callbacks may be omitted). No callback may occur after the task completes.
/// StopAsync must terminate only this session's owned process tree and unblock the sequence.
/// A session and its output remain available after completion until the application cleans them up.
/// </summary>
public interface ILaunchSession
{
    Task<IReadOnlyList<LaunchCommandResult>> RunSequenceAsync(IReadOnlyList<string> commands,
        Action<LaunchCommandResult> commandCompleted, CancellationToken cancellationToken);
    Task StopAsync();
}

public sealed record LaunchCommandResult(int Index, int ExitCode);

public enum ManagedLaunchState { Running, Succeeded, Failed, Stopped }
public enum ManagedProcessState { Pending, Running, Succeeded, Failed, Stopping, Stopped }
public enum ManagedStepState { Pending, Running, Succeeded, Failed, Skipped, Stopped }

public sealed record ManagedStepInfo(Guid StepId, string Command, ManagedStepState State, int? ExitCode, string? Error);
public sealed record ManagedProcessInfo(Guid ProcessId, Guid SessionId, string Name, string WorkingDirectory,
    ManagedProcessState State, string? Error, IReadOnlyList<ManagedStepInfo> Steps);
public sealed record ManagedLaunchInfo(Guid Id, Guid ProjectId, Guid ActionId, ManagedLaunchState State,
    IReadOnlyList<ManagedProcessInfo> Processes);

/// <summary>
/// In-memory launch owner. Start snapshots the selected action and returns identifiers
/// immediately; session creation and execution occur asynchronously. Instances retain
/// history for their lifetime. No arbitrary process IDs are accepted.
/// </summary>
public sealed class LaunchOrchestrator(ILaunchSessionFactory factory)
{
    private sealed class Step(Guid id, string command)
    {
        public Guid Id = id;
        public string Command = command;
        public ManagedStepState State;
        public int? ExitCode;
        public string? Error;
        public ManagedStepInfo Snapshot() => new(Id, Command, State, ExitCode, Error);
    }

    private sealed class Process(Guid id, string name, string directory, List<Step> steps)
    {
        public Guid Id = id;
        public Guid SessionId = Guid.NewGuid();
        public string Name = name;
        public string Directory = directory;
        public List<Step> Steps = steps;
        public ManagedProcessState State;
        public string? Error;
        public ILaunchSession? Session;
        public TaskCompletionSource<ILaunchSession?> Created = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancellation = new();
        public SemaphoreSlim StartGate = new(1, 1);
        public string? ProtocolError;
        public Task? StopTask;
        public ManagedProcessInfo Snapshot() => new(Id, SessionId, Name, Directory, State, Error,
            Steps.Select(s => s.Snapshot()).ToArray());
    }

    private sealed class Launch(Guid projectId, Guid actionId, List<Process> processes)
    {
        public Guid Id = Guid.NewGuid();
        public Guid ProjectId = projectId;
        public Guid ActionId = actionId;
        public List<Process> Processes = processes;
        public ManagedLaunchInfo Snapshot()
        {
            var state = Processes.Any(p => p.State is ManagedProcessState.Pending or ManagedProcessState.Running or ManagedProcessState.Stopping)
                ? ManagedLaunchState.Running
                : Processes.Any(p => p.State == ManagedProcessState.Failed) ? ManagedLaunchState.Failed
                : Processes.Any(p => p.State == ManagedProcessState.Stopped) ? ManagedLaunchState.Stopped
                : ManagedLaunchState.Succeeded;
            return new(Id, ProjectId, ActionId, state, Processes.Select(p => p.Snapshot()).ToArray());
        }
    }

    private readonly object gate = new();
    private readonly Dictionary<Guid, Launch> launches = [];
    private readonly ILaunchSessionFactory factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public ManagedLaunchInfo Start(ProjectConfiguration project, Guid actionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Id == Guid.Empty || project.Actions is null) throw new ArgumentException("A valid project and actions are required.", nameof(project));
        var matches = project.Actions.Where(a => a is not null && a.Id == actionId).ToArray();
        if (actionId == Guid.Empty || matches.Length != 1) throw new ArgumentException("Select exactly one action by ID.", nameof(actionId));
        var action = matches[0];
        if (action.Processes is null || action.Processes.Count == 0) throw new ArgumentException("The action requires processes.", nameof(project));
        var ids = new HashSet<Guid> { project.Id };
        if (!ids.Add(action.Id)) throw new ArgumentException("IDs must be unique.", nameof(project));
        var processes = new List<Process>();
        foreach (var process in action.Processes)
        {
            if (process is null || process.Id == Guid.Empty || !ids.Add(process.Id) ||
                string.IsNullOrWhiteSpace(process.Name) || string.IsNullOrWhiteSpace(process.WorkingDirectory) ||
                process.Steps is null || process.Steps.Count == 0)
                throw new ArgumentException("Each process requires a unique ID, name, working directory and steps.", nameof(project));
            var previous = new HashSet<Guid>();
            var steps = new List<Step>();
            foreach (var step in process.Steps)
            {
                if (step is null || step.Id == Guid.Empty || !ids.Add(step.Id) || string.IsNullOrWhiteSpace(step.Command) ||
                    (step.DependsOnSuccessOf is Guid dependency && !previous.Contains(dependency)))
                    throw new ArgumentException("Steps require unique IDs, commands, and dependencies on earlier steps in the same process.", nameof(project));
                previous.Add(step.Id);
                steps.Add(new(step.Id, step.Command));
            }
            processes.Add(new(process.Id, process.Name, process.WorkingDirectory, steps));
        }
        var launch = new Launch(project.Id, actionId, processes);
        ManagedLaunchInfo snapshot;
        lock (gate) { launches.Add(launch.Id, launch); snapshot = launch.Snapshot(); }
        foreach (var process in processes) _ = Task.Run(() => RunAsync(launch, process));
        return snapshot;
    }

    public IReadOnlyList<ManagedLaunchInfo> List()
    {
        lock (gate) return launches.Values.Select(l => l.Snapshot()).ToArray();
    }

    public ManagedLaunchInfo Get(Guid launchId)
    {
        lock (gate) return FindLaunch(launchId).Snapshot();
    }

    /// <summary>Requests a scoped stop. A failed stop is reported as failed and can be retried.</summary>
    public Task StopSessionAsync(Guid sessionId)
    {
        lock (gate)
        {
            var process = launches.Values.SelectMany(l => l.Processes).SingleOrDefault(p => p.SessionId == sessionId)
                ?? throw new KeyNotFoundException($"Unknown session ID: {sessionId}");
            return StopLocked(process);
        }
    }

    public Task StopLaunchAsync(Guid launchId)
    {
        lock (gate) return Task.WhenAll(FindLaunch(launchId).Processes.Select(StopLocked));
    }

    private Launch FindLaunch(Guid id) => launches.TryGetValue(id, out var launch)
        ? launch : throw new KeyNotFoundException($"Unknown launch ID: {id}");

    private Task StopLocked(Process process)
    {
        if (process.StopTask is { IsCompleted: false }) return process.StopTask;
        if (process.State == ManagedProcessState.Stopped) return Task.CompletedTask;
        process.State = ManagedProcessState.Stopping;
        // Cancellation callbacks and adapter code must never execute under the registry lock.
        return process.StopTask = Task.Run(async () =>
        {
            process.Cancellation.Cancel();
            var session = await process.Created.Task;
            await process.StartGate.WaitAsync();
            try
            {
                if (session is not null) await StopAdapterAsync(process, session);
                else lock (gate) { process.State = ManagedProcessState.Stopped; SkipPending(process); }
            }
            finally { process.StartGate.Release(); }
        });
    }

    private async Task StopAdapterAsync(Process process, ILaunchSession session)
    {
        try
        {
            await session.StopAsync();
            lock (gate)
            {
                process.State = ManagedProcessState.Stopped;
                process.Error = null;
                foreach (var step in process.Steps.Where(s => s.State == ManagedStepState.Running)) step.State = ManagedStepState.Stopped;
                SkipPending(process);
            }
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                process.State = ManagedProcessState.Failed;
                process.Error = ex.Message;
                foreach (var step in process.Steps.Where(s => s.State == ManagedStepState.Running))
                { step.State = ManagedStepState.Failed; step.Error = ex.Message; }
                SkipPending(process);
            }
        }
    }

    private async Task RunAsync(Launch launch, Process process)
    {
        try
        {
            var request = new LaunchSessionRequest(launch.Id, process.SessionId, launch.ProjectId, launch.ActionId,
                process.Id, process.Name, process.Directory);
            var session = await factory.CreateAsync(request, process.Cancellation.Token);
            lock (gate)
            {
                process.Session = session ?? throw new InvalidOperationException("Session factory returned null.");
            }
            process.Created.TrySetResult(session);
            Task<IReadOnlyList<LaunchCommandResult>> sequence;
            await process.StartGate.WaitAsync();
            try
            {
                lock (gate)
                {
                    if (process.State == ManagedProcessState.Stopping) return;
                    process.State = ManagedProcessState.Running;
                    process.Steps[0].State = ManagedStepState.Running;
                }
                sequence = session.RunSequenceAsync(process.Steps.Select(s => s.Command).ToArray(),
                    result => ReportCompletion(process, result), process.Cancellation.Token);
            }
            finally { process.StartGate.Release(); }
            var results = await sequence;
            lock (gate)
            {
                if (process.State != ManagedProcessState.Running) return;
                if (results is null) throw new InvalidOperationException("Sequence returned no results.");
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i] is null || results[i].Index != i)
                        throw new InvalidOperationException("Final results must be an ordered completed prefix.");
                    ApplyCompletion(process, results[i]);
                }
                if (process.ProtocolError is not null) throw new InvalidOperationException(process.ProtocolError);
                if (results.Count != process.Steps.Count(s => s.ExitCode.HasValue))
                    throw new InvalidOperationException("Final results do not match completion callbacks.");
                if (process.Steps.Any(s => s.State == ManagedStepState.Failed))
                    process.State = ManagedProcessState.Failed;
                else if (process.Steps.All(s => s.State == ManagedStepState.Succeeded))
                    process.State = ManagedProcessState.Succeeded;
                else throw new InvalidOperationException("Sequence ended before completing all commands.");
            }
        }
        catch (Exception ex)
        {
            process.Created.TrySetResult(null);
            lock (gate)
            {
                var current = process.Steps.FirstOrDefault(s => s.State == ManagedStepState.Running);
                bool stopping = process.State is ManagedProcessState.Stopping or ManagedProcessState.Stopped;
                if (current is not null) { current.State = stopping ? ManagedStepState.Stopped : ManagedStepState.Failed; if (!stopping) current.Error = ex.Message; }
                if (process.State == ManagedProcessState.Stopping && process.Session is null) process.State = ManagedProcessState.Stopped;
                else if (!stopping) { process.State = ManagedProcessState.Failed; process.Error = ex.Message; }
                SkipPending(process);
            }
        }
    }

    private void ReportCompletion(Process process, LaunchCommandResult result)
    {
        lock (gate)
        {
            if (process.State == ManagedProcessState.Running) ApplyCompletion(process, result);
        }
    }

    private static void ApplyCompletion(Process process, LaunchCommandResult result)
    {
        if (result is null || result.Index < 0 || result.Index >= process.Steps.Count)
        { process.ProtocolError = "Invalid command result index."; return; }
        var step = process.Steps[result.Index];
        if (step.ExitCode.HasValue)
        {
            if (step.ExitCode != result.ExitCode) process.ProtocolError = "Conflicting command results.";
            return;
        }
        if (step.State != ManagedStepState.Running)
        { process.ProtocolError = "Command results must be sequential and stop after failure."; return; }
        step.ExitCode = result.ExitCode;
        step.State = result.ExitCode == 0 ? ManagedStepState.Succeeded : ManagedStepState.Failed;
        if (result.ExitCode != 0) SkipPending(process);
        else if (result.Index + 1 < process.Steps.Count)
            process.Steps[result.Index + 1].State = ManagedStepState.Running;
    }

    private static void SkipPending(Process process)
    {
        foreach (var step in process.Steps.Where(s => s.State == ManagedStepState.Pending)) step.State = ManagedStepState.Skipped;
    }
}
