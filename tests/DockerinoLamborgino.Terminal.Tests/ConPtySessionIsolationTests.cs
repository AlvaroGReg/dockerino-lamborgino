using DockerinoLamborgino.Terminal;
using EasyWindowsTerminalControl;
using EasyWindowsTerminalControl.Internals;
using System.IO;
using Xunit;

namespace DockerinoLamborgino.Terminal.Tests;

public class ConPtySessionIsolationTests
{
    [Fact]
    public async Task Stopping_one_conpty_session_leaves_the_other_process_alive()
    {
        using var first = new Session();
        using var second = new Session();

        await Task.WhenAll(first.StartAsync(), second.StartAsync());
        Assert.NotEqual(first.ProcessId, second.ProcessId);
        Assert.False(first.Process.HasExited);
        Assert.False(second.Process.HasExited);

        first.Stop();
        await WaitUntilAsync(() => first.Process.HasExited, "first process to exit");
        await Task.Delay(500);
        Assert.False(second.Process.HasExited);
        Assert.False(second.Run.IsCompleted);

        second.Stop();
        await WaitUntilAsync(() => second.Process.HasExited, "second process to exit");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (!condition())
                await Task.Delay(50, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for {description}.");
        }
    }

    private sealed class Session : IDisposable
    {
        private readonly JobObjectProcessFactory _factory = new();
        private readonly TermPTY _terminal = new();
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Session()
        {
            // No terminal control or window is needed; discard output before it reaches the UI.
            _terminal.InterceptOutputToUITerminal = (ref Span<char> output) => output = Span<char>.Empty;
            _terminal.TermReady += (_, _) => _ready.TrySetResult();
        }

        public Task Run { get; private set; } = Task.CompletedTask;
        public IProcess Process => _terminal.Process ?? throw new InvalidOperationException("No terminal process.");
        public int ProcessId => ((JobObjectProcessFactory.IProcessDiagnostics)Process).ProcessId;

        public async Task StartAsync()
        {
            var command = $"\"{Path.Combine(Environment.SystemDirectory, "cmd.exe")}\" /Q /K";
            Run = Task.Run(() => _terminal.Start(command, logOutput: true, factory: _factory));
            var completed = await Task.WhenAny(_ready.Task, Run).WaitAsync(TimeSpan.FromSeconds(15));
            if (completed == Run)
            {
                await Run;
                throw new InvalidOperationException("Terminal stopped before becoming ready.");
            }
            await _ready.Task;
        }

        public void Stop() => _factory.Dispose();

        public void Dispose()
        {
            _factory.Dispose();
            // Observe background failures without waiting indefinitely during cleanup.
            _ = Run.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
