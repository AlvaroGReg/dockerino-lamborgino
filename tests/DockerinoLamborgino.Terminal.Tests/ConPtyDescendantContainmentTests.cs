using System.Diagnostics;
using System.IO;
using System.Text;
using DockerinoLamborgino.Terminal;
using EasyWindowsTerminalControl;
using EasyWindowsTerminalControl.Internals;
using Xunit;

namespace DockerinoLamborgino.Terminal.Tests;

public class ConPtyDescendantContainmentTests
{
    [Fact]
    public async Task Stop_and_owner_disposal_terminate_only_their_own_immediate_descendants()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dockerino-containment-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "tree.ps1");
        await File.WriteAllTextAsync(script, """
            param([string]$Directory, [string]$Name, [int]$Depth)
            [System.IO.File]::WriteAllText((Join-Path $Directory "$Name-$Depth.pid"), "$PID")
            if ($Depth -lt 2) {
                $next = $Depth + 1
                Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @(
                    '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
                    '-Directory', "`"$Directory`"", '-Name', $Name, '-Depth', $next
                ) | Out-Null
            }
            # A bounded lifetime prevents an escaped test process from running indefinitely.
            Start-Sleep -Seconds 120
            """);

        try
        {
            using var first = new Session(script, directory, "first");
            using var second = new Session(script, directory, "second");
            await Task.WhenAll(first.StartAsync(), second.StartAsync());
            try
            {
                await WaitUntilAsync(() => File.Exists(Path.Combine(directory, "first-1.pid")));
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException($"First terminal output: {first.Output}");
            }
            using var firstChild = await OpenProcessAsync(directory, "first", 1);
            using var firstGrandchild = await OpenProcessAsync(directory, "first", 2);
            using var secondChild = await OpenProcessAsync(directory, "second", 1);
            using var secondGrandchild = await OpenProcessAsync(directory, "second", 2);
            Assert.All(new[] { firstChild, firstGrandchild, secondChild, secondGrandchild },
                process => Assert.False(process.HasExited));

            first.Stop();
            await WaitUntilAsync(() => first.Process.HasExited && firstChild.HasExited && firstGrandchild.HasExited);
            Assert.False(second.Process.HasExited);
            Assert.False(secondChild.HasExited);
            Assert.False(secondGrandchild.HasExited);

            // Disposing the session owner is the Job Object cleanup mechanism,
            // not a substitute for exercising the application's shutdown path.
            second.Dispose();
            await WaitUntilAsync(() => second.Process.HasExited && secondChild.HasExited && secondGrandchild.HasExited);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<Process> OpenProcessAsync(string directory, string name, int depth)
    {
        var path = Path.Combine(directory, $"{name}-{depth}.pid");
        int id = 0;
        await WaitUntilAsync(() => File.Exists(path) && int.TryParse(File.ReadAllText(path), out id));
        // Keep the process handle: checking HasExited on it avoids PID reuse.
        var process = Process.GetProcessById(id);
        _ = process.Handle;
        return process;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (!condition())
                await Task.Delay(50, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for the test process tree.");
        }
    }

    private sealed class Session : IDisposable
    {
        private readonly JobObjectProcessFactory _factory = new();
        private readonly TermPTY _terminal = new();
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _command;
        private readonly StringBuilder _output = new();
        public string Output { get { lock (_output) return _output.ToString(); } }

        public Session(string script, string directory, string name)
        {
            var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            _command = $"\"{shell}\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" -Directory \"{directory}\" -Name {name} -Depth 0";
            _terminal.InterceptOutputToUITerminal = (ref Span<char> output) =>
            {
                lock (_output) _output.Append(output);
                output = Span<char>.Empty;
            };
            _terminal.TermReady += (_, _) => _ready.TrySetResult();
        }

        public Task Run { get; private set; } = Task.CompletedTask;
        public IProcess Process => _terminal.Process ?? throw new InvalidOperationException("No terminal process.");

        public async Task StartAsync()
        {
            Run = Task.Run(() => _terminal.Start(_command, logOutput: true, factory: _factory));
            var completed = await Task.WhenAny(_ready.Task, Run).WaitAsync(TimeSpan.FromSeconds(15));
            if (completed == Run)
            {
                await Run;
                throw new InvalidOperationException("Terminal stopped before becoming ready.");
            }
            await _ready.Task;
        }

        public void Stop() => Process.Kill();

        public void Dispose()
        {
            _factory.Dispose();
            _ = Run.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
