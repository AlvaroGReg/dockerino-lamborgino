using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DockerinoLamborgino.Terminal;
using EasyWindowsTerminalControl;
using EasyWindowsTerminalControl.Internals;

namespace DockerinoLamborgino.App;

public enum TerminalSessionState
{
    Starting,
    Running,
    Exited,
    Failed,
    Stopped
}

/// <summary>A single-use terminal session. Start and Stop must be called on its dispatcher.</summary>
public sealed class TerminalSessionView : UserControl, IDisposable
{
    private readonly EasyTerminalControl _terminal = new();
    private readonly object _outputLock = new();
    private readonly StringBuilder _earlyOutput = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GuardedFactory? _factory;
    private TermPTY? _term;
    private Task? _run;
    private DispatcherTimer? _timer;
    private Window? _window;
    private bool _connected;
    private bool _started;
    private bool _isStopped;

    public TerminalSessionView()
    {
        _terminal.StartupCommandLine = "pwsh.exe -NoLogo -NoProfile -NoExit";
        _terminal.DisconnectConPTYTerm();
        Content = _terminal;
        Loaded += OnLoaded;
    }

    public event Action<string>? StatusChanged;
    public event Action<TerminalSessionState>? StateChanged;
    public string Status { get; private set; } = "Ready";
    public TerminalSessionState? State { get; private set; }
    public int? ExitCode { get; private set; }

    public void Start()
    {
        Dispatcher.VerifyAccess();
        if (_started || _isStopped) return;
        _started = true;
        SetState(TerminalSessionState.Starting, "Starting terminal...");
        // A status subscriber may synchronously stop the session.
        if (_isStopped) return;
        StartCore();
    }

    private async void StartCore()
    {
        try
        {
            _factory = new GuardedFactory();
            _term = new TermPTY();
            var term = _term;
            var factory = _factory;
            term.InterceptOutputToUITerminal = (ref Span<char> output) =>
            {
                lock (_outputLock)
                {
                    if (!_connected)
                    {
                        _earlyOutput.Append(output);
                        output = Span<char>.Empty;
                    }
                }
            };
            term.TermReady += (_, _) => _ready.TrySetResult();
            var command = _terminal.StartupCommandLine;
            var directory = _terminal.WorkingDirectory;
            var run = Task.Run(() => term.Start(command, logOutput: true, factory: factory, workingDirectory: directory));
            _run = run;
            // Observe errors even if Stop or the timeout wins the race.
            _ = run.ContinueWith(t =>
            {
                if (t.IsFaulted) _ready.TrySetException(t.Exception!.GetBaseException());
                else _ready.TrySetResult();
            }, TaskScheduler.Default);

            var winner = await Task.WhenAny(_ready.Task, _stopped.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            if (_isStopped || winner == _stopped.Task) return;
            if (winner != _ready.Task) throw new TimeoutException("Terminal did not start within 15 seconds.");
            await _ready.Task;
            if (_isStopped) return;
            if (run.IsCompleted)
            {
                await run;
                UpdateProcessStatus();
                if (!_isStopped) Finish(TerminalSessionState.Failed, "Terminal ended before becoming ready.");
                return;
            }

            // The control must not attach until Start has created the process.
            _terminal.ConPTYTerm = term;
            lock (_outputLock)
            {
                if (_earlyOutput.Length > 0) term.WriteToUITerminal(_earlyOutput.ToString());
                _earlyOutput.Clear();
                _connected = true;
            }
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += OnTimer;
            _timer.Start();
            UpdateProcessStatus();
            _ = ObserveRunAsync(run);
        }
        catch (Exception exception)
        {
            if (!_isStopped) Finish(TerminalSessionState.Failed, $"Terminal failed: {exception.Message}");
        }
    }

    private async Task ObserveRunAsync(Task run)
    {
        try
        {
            await run;
            if (!_isStopped)
            {
                UpdateProcessStatus();
                if (!_isStopped) Finish(TerminalSessionState.Failed, "Terminal ended unexpectedly.");
            }
        }
        catch (Exception exception)
        {
            if (!_isStopped) Finish(TerminalSessionState.Failed, $"Terminal failed: {exception.Message}");
        }
    }

    private void OnTimer(object? sender, EventArgs e) => UpdateProcessStatus();

    private void UpdateProcessStatus()
    {
        if (_isStopped) return;
        try
        {
            var process = _factory?.Process;
            if (process is null) return;
            if (process.HasExited)
            {
                ExitCode = process.ExitCode;
                Finish(TerminalSessionState.Exited, ExitCode is int code
                    ? $"Terminal exited (code {code})."
                    : "Terminal exited.");
            }
            else
                SetState(TerminalSessionState.Running, "Terminal running.");
        }
        catch (Exception exception)
        {
            Finish(TerminalSessionState.Failed, $"Terminal failed: {exception.Message}");
        }
    }

    public void Stop()
    {
        Dispatcher.VerifyAccess();
        if (!_isStopped) Finish(TerminalSessionState.Stopped, "Terminal stopped.");
    }

    public void Dispose() => Stop();

    private void Finish(TerminalSessionState state, string status)
    {
        if (_isStopped) return;
        _isStopped = true;
        _stopped.TrySetResult();
        _timer?.Stop();
        if (_window is not null) _window.Closed -= OnWindowClosed;
        Loaded -= OnLoaded;
        IsEnabled = false;
        var factory = _factory;
        var term = _term;
        var run = _run;
        // Close the job before reporting stop or allowing application exit.
        // This also serializes with a still-pending process creation.
        factory?.Dispose();
        _ = Task.Run(async () =>
        {
            // TermPTY owns the process and pseudoconsole; closing streams helps
            // its reader exit after initialization finishes or fails.
            if (run is not null) await Task.WhenAny(_ready.Task, run).ConfigureAwait(false);
            try { term?.CloseStdinToApp(); } catch (Exception) { }
            try { term?.ConsoleOutStream?.Dispose(); } catch (Exception) { }
        });
        SetState(state, status);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window == _window) return;
        if (_window is not null) _window.Closed -= OnWindowClosed;
        _window = window;
        if (_window is not null) _window.Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e) => Stop();

    private void SetState(TerminalSessionState state, string status)
    {
        if (State == state && Status == status) return;
        State = state;
        Status = status;
        StateChanged?.Invoke(state);
        // A state subscriber can synchronously stop the session.
        if (State != state) return;
        StatusChanged?.Invoke(status);
    }

    private sealed class GuardedFactory : IProcessFactory, IDisposable
    {
        private readonly object _sync = new();
        private readonly JobObjectProcessFactory _inner = new();
        private bool _disposed;
        private TrackedProcess? _process;
        public TrackedProcess? Process => Volatile.Read(ref _process);

        public IProcess Start(string command, nuint attributes, PseudoConsole console, string? workingDirectory = null)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var process = new TrackedProcess(_inner.Start(command, attributes, console, workingDirectory));
                Volatile.Write(ref _process, process);
                return process;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _inner.Dispose();
            }
        }
    }

    // TermPTY disposes its IProcess before Start returns. Retain diagnostics so
    // the dispatcher can still classify an exit after that disposal.
    private sealed class TrackedProcess : IProcess, JobObjectProcessFactory.IProcessDiagnostics
    {
        private readonly IProcess _inner;
        private readonly object _sync = new();
        private bool _disposed;
        private int? _exitCode;

        public TrackedProcess(IProcess inner) => _inner = inner;
        public int ProcessId => ((JobObjectProcessFactory.IProcessDiagnostics)_inner).ProcessId;
        public int? ExitCode
        {
            get
            {
                lock (_sync)
                {
                    if (!_disposed)
                        _exitCode = ((JobObjectProcessFactory.IProcessDiagnostics)_inner).ExitCode;
                    return _exitCode;
                }
            }
        }
        public bool HasExited => ExitCode.HasValue;
        public void WaitForExit() => _inner.WaitForExit();
        public void Kill(bool EntireProcessTree = false) => _inner.Kill(EntireProcessTree);
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                try { _exitCode = ((JobObjectProcessFactory.IProcessDiagnostics)_inner).ExitCode; }
                finally
                {
                    _disposed = true;
                    _inner.Dispose();
                }
            }
        }
    }
}
