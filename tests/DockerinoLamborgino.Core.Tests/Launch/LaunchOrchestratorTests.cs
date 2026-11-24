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
        public Func<Task> Stop = () => Task.CompletedTask;
        public Func<CancellationToken, Task> Ready = _ => Task.CompletedTask;
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
        public Task StopAsync() { Interlocked.Increment(ref Stops); return Stop(); }
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => Ready(cancellationToken);
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
    public async Task AsynchronousSequenceFailureBeforeReadinessIsReported()
    {
        var (project, action) = Config(["one"]);
        var preparation = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeSession
        {
            Run = (_, _) => preparation.Task,
            Ready = token => Task.Delay(Timeout.Infinite, token)
        };
        var factory = new Factory();
        factory.Sessions[action.Processes[0].Id] = session;
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => session.Commands.Count == 1);
        preparation.SetException(new InvalidOperationException("preparation failed"));
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Failed);
        Assert.Contains("preparation failed", orchestrator.Get(launch.Id).Processes[0].Error);
    }

    [Fact]
    public async Task ResetReportsAsynchronousSequenceFailureBeforeReadiness()
    {
        var (project, action) = Config(["one"]);
        var preparation = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = new FakeSession
        {
            Run = (_, _) => preparation.Task,
            Ready = token => Task.Delay(Timeout.Infinite, token)
        };
        var count = 0;
        var factory = new Factory { Create = (_, _) => Task.FromResult<ILaunchSession>(
            Interlocked.Increment(ref count) == 1 ? new FakeSession() : replacement) };
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        var reset = orchestrator.ResetSessionAsync(launch.Processes[0].SessionId);
        await WaitUntil(() => replacement.Commands.Count == 1);
        preparation.SetException(new InvalidOperationException("preparation failed"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reset.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("preparation failed", error.Message);
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

    [Fact]
    public async Task ResetReplaysSnapshotAndLeavesOtherProcessAlone()
    {
        var (project, action) = Config(["one", "two"], ["other"]);
        var sessions = new ConcurrentDictionary<Guid, FakeSession>();
        var requests = new ConcurrentQueue<LaunchSessionRequest>();
        var factory = new Factory { Create = (request, _) =>
        {
            requests.Enqueue(request);
            var session = new FakeSession();
            sessions[request.SessionId] = session;
            return Task.FromResult<ILaunchSession>(session);
        } };
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        var original = launch.Processes[0];
        action.Processes[0].WorkingDirectory = "changed";
        action.Processes[0].Steps[0].Command = "changed";

        var replacement = await orchestrator.ResetSessionAsync(original.SessionId);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        var current = orchestrator.Get(launch.Id);
        Assert.NotEqual(original.SessionId, replacement.SessionId);
        Assert.Equal(original.ProcessId, replacement.ProcessId);
        Assert.Equal("directory", replacement.WorkingDirectory);
        Assert.Equal(new[] { "one", "two" }, sessions[replacement.SessionId].Commands);
        Assert.Equal(original.Steps.Select(s => s.StepId), replacement.Steps.Select(s => s.StepId));
        Assert.Equal(launch.Processes[1].SessionId, current.Processes[1].SessionId);
        Assert.Equal(0, sessions[current.Processes[1].SessionId].Stops);
        Assert.Equal(1, sessions[original.SessionId].Stops);
        Assert.Equal(3, requests.Count);
    }

    [Fact]
    public async Task ConcurrentResetsShareOneReplacement()
    {
        var (project, action) = Config(["one"]);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeSession { Stop = () => release.Task };
        int creates = 0;
        var factory = new Factory { Create = (_, _) =>
            Task.FromResult<ILaunchSession>(Interlocked.Increment(ref creates) == 1 ? session : new FakeSession()) };
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        var first = orchestrator.ResetSessionAsync(launch.Processes[0].SessionId);
        var second = orchestrator.ResetSessionAsync(launch.Processes[0].SessionId);
        await WaitUntil(() => session.Stops == 1);
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Equal(results[0].SessionId, results[1].SessionId);
        Assert.Equal(2, creates);
    }

    [Fact]
    public async Task ReservationDoesNotStopUntilStartedAndOutcomesAreInspectable()
    {
        var (project, action) = Config(["one"]);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new FakeSession { Stop = () => release.Task };
        int creates = 0;
        var orchestrator = new LaunchOrchestrator(new Factory { Create = (_, _) =>
            Task.FromResult<ILaunchSession>(Interlocked.Increment(ref creates) == 1 ? first : new FakeSession()) });
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        var id = launch.Processes[0].SessionId;
        var reservation = orchestrator.ReserveReset(id);
        var duplicate = orchestrator.ReserveReset(id);
        Assert.Equal(launch.Id, reservation.Acceptance.LaunchId);
        Assert.Equal(id, reservation.Acceptance.OriginalSessionId);
        Assert.Same(reservation.Completion, duplicate.Completion);
        Assert.Equal(0, first.Stops);
        Assert.Equal("pending", Assert.Single(orchestrator.Get(launch.Id).Resets).State);
        reservation.Start();
        duplicate.Start();
        await WaitUntil(() => first.Stops == 1);
        Assert.False(reservation.Completion.IsCompleted);
        release.SetResult();
        var replacement = await reservation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var outcome = Assert.Single(orchestrator.Get(launch.Id).Resets);
        Assert.Equal("succeeded", outcome.State);
        Assert.Equal(replacement.SessionId, outcome.ReplacementSessionId);
        Assert.Throws<KeyNotFoundException>(() => orchestrator.ReserveReset(id));
    }

    [Fact]
    public async Task ExplicitSessionStopCancelsReservedReset()
    {
        var (project, action) = Config(["one"]);
        var orchestrator = new LaunchOrchestrator(new Factory());
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        var id = launch.Processes[0].SessionId;
        var reservation = orchestrator.ReserveReset(id);
        await orchestrator.StopSessionAsync(id);
        reservation.Start();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reservation.Completion);
        var outcome = Assert.Single(orchestrator.Get(launch.Id).Resets);
        Assert.Equal("failed", outcome.State);
        Assert.Contains("cancel", outcome.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(id, orchestrator.Get(launch.Id).Processes[0].SessionId);
    }

    [Fact]
    public async Task ResetStopFailurePreservesOriginalAndCanBeRetried()
    {
        var (project, action) = Config(["one"]);
        var session = new FakeSession { Stop = () => Task.FromException(new InvalidOperationException("stop failed")) };
        int creates = 0;
        var factory = new Factory { Create = (_, _) =>
            Task.FromResult<ILaunchSession>(Interlocked.Increment(ref creates) == 1 ? session : new FakeSession()) };
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ResetSessionAsync(launch.Processes[0].SessionId));
        var failed = orchestrator.Get(launch.Id).Processes[0];
        Assert.Equal(launch.Processes[0].SessionId, failed.SessionId);
        Assert.Equal(ManagedProcessState.Failed, failed.State);
        Assert.Contains("stop failed", failed.Error);
        var outcome = Assert.Single(orchestrator.Get(launch.Id).Resets);
        Assert.Equal("failed", outcome.State);
        Assert.Contains("stop failed", outcome.Error);
        Assert.Equal(1, creates);
        session.Stop = () => Task.CompletedTask;
        var replacement = await orchestrator.ResetSessionAsync(failed.SessionId);
        Assert.NotEqual(failed.SessionId, replacement.SessionId);
    }

    [Fact]
    public async Task ResetStartFailureIsInspectable()
    {
        var (project, action) = Config(["one"]);
        int creates = 0;
        var factory = new Factory { Create = (_, _) =>
            Interlocked.Increment(ref creates) == 1
                ? Task.FromResult<ILaunchSession>(new FakeSession())
                : Task.FromException<ILaunchSession>(new InvalidOperationException("start failed")) };
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ResetSessionAsync(launch.Processes[0].SessionId));
        var failed = orchestrator.Get(launch.Id).Processes[0];
        Assert.NotEqual(launch.Processes[0].SessionId, failed.SessionId);
        Assert.Equal(ManagedProcessState.Failed, failed.State);
        Assert.Contains("start failed", failed.Error);
        var outcome = Assert.Single(orchestrator.Get(launch.Id).Resets);
        Assert.Equal("failed", outcome.State);
        Assert.Contains("start failed", outcome.Error);
    }

    [Fact]
    public async Task StopLaunchWhileResetIsStoppingPreventsReplacement()
    {
        var (project, action) = Config(["one"]);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeSession { Stop = () => release.Task };
        int creates = 0;
        var factory = new Factory { Create = (_, _) =>
        {
            Interlocked.Increment(ref creates);
            return Task.FromResult<ILaunchSession>(session);
        } };
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        var reset = orchestrator.ResetSessionAsync(launch.Processes[0].SessionId);
        await WaitUntil(() => session.Stops == 1);
        var stop = orchestrator.StopLaunchAsync(launch.Id);
        release.SetResult();
        await stop;
        await Assert.ThrowsAsync<InvalidOperationException>(() => reset);
        Assert.Equal(1, creates);
        Assert.Equal(launch.Processes[0].SessionId, orchestrator.Get(launch.Id).Processes[0].SessionId);
        Assert.Equal(ManagedLaunchState.Stopped, orchestrator.Get(launch.Id).State);
    }

    [Fact]
    public async Task ResetWaitsForReadinessAndStopDoesNotDeadlock()
    {
        var (project, action) = Config(["one"]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = new FakeSession
        {
            Ready = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
        };
        int creates = 0;
        var factory = new Factory { Create = (_, _) => Task.FromResult<ILaunchSession>(
            Interlocked.Increment(ref creates) == 1 ? new FakeSession() : replacement) };
        var orchestrator = new LaunchOrchestrator(factory);
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        var reset = orchestrator.ResetSessionAsync(launch.Processes[0].SessionId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(reset.IsCompleted);
        await orchestrator.StopLaunchAsync(launch.Id).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reset);
        Assert.Equal(1, replacement.Stops);
        Assert.Equal(ManagedLaunchState.Stopped, orchestrator.Get(launch.Id).State);
    }

    [Fact]
    public async Task ReplacementCannotBeResetWhileItsStartupIsPending()
    {
        var (project, action) = Config(["one"]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = new FakeSession
        {
            Ready = async token => { entered.SetResult(); await ready.Task.WaitAsync(token); }
        };
        var creates = 0;
        var orchestrator = new LaunchOrchestrator(new Factory { Create = (_, _) =>
            Task.FromResult<ILaunchSession>(Interlocked.Increment(ref creates) == 1 ? new FakeSession() : replacement) });
        var launch = orchestrator.Start(project, action.Id);
        await WaitUntil(() => orchestrator.Get(launch.Id).State == ManagedLaunchState.Succeeded);
        var reset = orchestrator.ResetSessionAsync(launch.Processes[0].SessionId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var replacementId = orchestrator.Get(launch.Id).Processes[0].SessionId;
        Assert.Throws<InvalidOperationException>(() => orchestrator.ReserveReset(replacementId));
        Assert.Equal(2, creates);
        await orchestrator.StopSessionAsync(launch.Processes[0].SessionId).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reset.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(ManagedProcessState.Stopped, orchestrator.Get(launch.Id).Processes[0].State);
    }
}
