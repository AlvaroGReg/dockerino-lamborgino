using System.Collections.Concurrent;
using DockerinoLamborgino.Core;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public sealed class LaunchOrchestratorTests
{
    private sealed class FakeSession : ILaunchSession
    {
        public ConcurrentQueue<string> Commands = new();
        public Func<string, CancellationToken, Task<int>> Run = (_, _) => Task.FromResult(0);
        public int Stops;
        public async Task<IReadOnlyList<LaunchCommandResult>> RunSequenceAsync(IReadOnlyList<string> commands,
            Action<LaunchCommandResult> commandCompleted, CancellationToken cancellationToken)
        {
            var results = new List<LaunchCommandResult>();
            foreach (var command in commands)
            {
                Commands.Enqueue(command);
                var result = new LaunchCommandResult(results.Count, await Run(command, cancellationToken));
                results.Add(result);
                commandCompleted(result);
                if (result.ExitCode != 0) break;
            }
            return results;
        }
        public Task StopAsync() { Interlocked.Increment(ref Stops); return Task.CompletedTask; }
    }

    private sealed class Factory : ILaunchSessionFactory
    {
        public ConcurrentDictionary<Guid, FakeSession> Sessions = new();
        public Func<LaunchSessionRequest, CancellationToken, Task<ILaunchSession>>? Create;
        public Task<ILaunchSession> CreateAsync(LaunchSessionRequest request, CancellationToken cancellationToken) =>
            Create?.Invoke(request, cancellationToken) ?? Task.FromResult<ILaunchSession>(
                Sessions.GetOrAdd(request.ProcessId, _ => new FakeSession()));
    }

    private sealed class ResultsOnlySession(IReadOnlyList<LaunchCommandResult> results) : ILaunchSession
    {
        public Task<IReadOnlyList<LaunchCommandResult>> RunSequenceAsync(IReadOnlyList<string> commands,
            Action<LaunchCommandResult> commandCompleted, CancellationToken cancellationToken) => Task.FromResult(results);
        public Task StopAsync() => Task.CompletedTask;
    }

    private static (ProjectConfiguration Project, LaunchAction Action) Config(params string[][] commands)
    {
        var action = new LaunchAction { Id = Guid.NewGuid(), Name = "run", Color = "#000000",
            Processes = commands.Select((list, i) => new LaunchProcess
            {
                Id = Guid.NewGuid(), Name = $"p{i}", WorkingDirectory = "directory",
                Steps = list.Select(command => new LaunchStep { Id = Guid.NewGuid(), Command = command }).ToList()
            }).ToList() };
        return (new ProjectConfiguration { Id = Guid.NewGuid(), Actions = [action] }, action);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task ProcessesRunIndependentlyAndFailureSkipsLaterSteps()
    {
        var (project, action) = Config(["first", "second", "never"], ["independent"]);
        var factory = new Factory();
        var first = new FakeSession();
        var blocked = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Run = (command, _) => command == "first" ? blocked.Task : Task.FromResult(7);
        factory.Sessions[action.Processes[0].Id] = first;
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => first.Commands.Contains("first") && orchestrator.Get(launch.Id).Processes[1].State == ManagedProcessState.Succeeded);
        Assert.Equal(new[] { "first" }, first.Commands);
        blocked.SetResult(0);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Failed);
        var result = orchestrator.Get(launch.Id);
        Assert.Equal(new[] { ManagedStepState.Succeeded, ManagedStepState.Failed, ManagedStepState.Skipped },
            result.Processes[0].Steps.Select(s => s.State));
        Assert.Equal(7, result.Processes[0].Steps[1].ExitCode);
        Assert.Equal(new[] { "first", "second" }, first.Commands);
    }

    [Fact]
    public async Task StopIsScopedAndSkipsRemainingSteps()
    {
        var (project, action) = Config(["block", "later"], ["other"]);
        var factory = new Factory();
        var session = new FakeSession { Run = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return 0; } };
        factory.Sessions[action.Processes[0].Id] = session;
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => session.Commands.Count == 1 && orchestrator.Get(launch.Id).Processes[1].State == ManagedProcessState.Succeeded);
        await orchestrator.StopSessionAsync(launch.Processes[0].SessionId);
        var result = orchestrator.Get(launch.Id);
        Assert.Equal(ManagedLaunchState.Stopped, result.State);
        Assert.Equal(ManagedStepState.Stopped, result.Processes[0].Steps[0].State);
        Assert.Equal(ManagedStepState.Skipped, result.Processes[0].Steps[1].State);
        Assert.Equal(1, session.Stops);
        Assert.Equal(0, factory.Sessions[action.Processes[1].Id].Stops);
        await orchestrator.StopLaunchAsync(launch.Id);
        Assert.Equal(1, factory.Sessions[action.Processes[1].Id].Stops);
        Assert.Equal(ManagedStepState.Succeeded, orchestrator.Get(launch.Id).Processes[1].Steps[0].State);
    }

    [Fact]
    public async Task StopDuringCreationWaitsForCleanup()
    {
        var (project, action) = Config(["one"]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Factory { Create = async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        } };
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await entered.Task;
        await orchestrator.StopLaunchAsync(launch.Id);
        Assert.Equal(ManagedLaunchState.Stopped, orchestrator.Get(launch.Id).State);
    }

    [Fact]
    public async Task DependenciesAreRedundantButMustReferToEarlierSteps()
    {
        var (project, action) = Config(["one", "two", "three"]);
        var steps = action.Processes[0].Steps;
        steps[2].DependsOnSuccessOf = steps[0].Id;
        var orchestrator = new LaunchOrchestrator(new Factory());
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        steps[0].DependsOnSuccessOf = steps[2].Id;
        Assert.Throws<ArgumentException>(() => orchestrator.Start(project, action.Id));
        Assert.Single(orchestrator.List());
    }

    [Fact]
    public async Task FinalResultsFillMissingCallbacksAndRejectInvalidSequences()
    {
        var (project, action) = Config(["one", "two", "three"]);
        var factory = new Factory { Create = (_, _) => Task.FromResult<ILaunchSession>(
            new ResultsOnlySession([new(0, 0), new(1, 9)])) };
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Failed);
        Assert.Equal(new[] { ManagedStepState.Succeeded, ManagedStepState.Failed, ManagedStepState.Skipped },
            orchestrator.Get(launch.Id).Processes[0].Steps.Select(s => s.State));

        factory.Create = (_, _) => Task.FromResult<ILaunchSession>(
            new ResultsOnlySession([new(0, 1), new(1, 0)]));
        var invalid = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(invalid.Id).State == ManagedLaunchState.Failed);
        Assert.NotNull(orchestrator.Get(invalid.Id).Processes[0].Error);
    }
}
