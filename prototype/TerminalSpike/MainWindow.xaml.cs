using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyWindowsTerminalControl;

namespace TerminalSpike;

public partial class MainWindow : Window
{
    private JobObjectProcessFactory? _processFactory;
    private TermPTY? _term;
    private string _command = "pwsh.exe -NoLogo -NoProfile -NoExit";
    private string? _workingDirectory;
    private string? _logPath;
    private string? _windowTitle;
    private DispatcherTimer? _processTimer;
    private bool _termReady;
    private string? _startError;
    private bool _processStartedLogged;
    private bool _processExitLogged;
    private readonly object _logLock = new();
    private readonly object _preconnectionOutputLock = new();
    private readonly System.Text.StringBuilder _preconnectionOutput = new();
    private readonly TaskCompletionSource _termReadySource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _sessionStarting;
    private bool _terminalConnected;

    public MainWindow()
    {
        InitializeComponent();
        ReadCommandLineOptions();
        Terminal.StartupCommandLine = _command;
        Terminal.WorkingDirectory = _workingDirectory;
        // Prevent EasyTerminalControl from starting its built-in TermPTY on Loaded.
        Terminal.DisconnectConPTYTerm();
        if (!string.IsNullOrWhiteSpace(_windowTitle))
        {
            Title = _windowTitle;
        }

        Closed += (_, _) =>
        {
            _processTimer?.Stop();
            _processFactory?.Dispose();
        };
        PrepareSession();
        Loaded += (_, _) => StartSession();
    }

    private void ReadCommandLineOptions()
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] == "--command" && index + 1 < arguments.Length)
            {
                _command = arguments[++index];
            }
            else if (arguments[index] == "--working-directory" && index + 1 < arguments.Length)
            {
                _workingDirectory = arguments[++index];
            }
            else if (arguments[index] == "--log-file" && index + 1 < arguments.Length)
            {
                _logPath = arguments[++index];
            }
            else if (arguments[index] == "--title" && index + 1 < arguments.Length)
            {
                _windowTitle = arguments[++index];
            }
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) => StartSession();

    private void PrepareSession()
    {
        try
        {
            _processFactory = new JobObjectProcessFactory();
            _term = new TermPTY();
            var term = _term;
            term.InterceptOutputToUITerminal = (ref Span<char> output) =>
            {
                var text = output.ToString();
                TryLog(text);
                lock (_preconnectionOutputLock)
                {
                    if (!_terminalConnected)
                    {
                        _preconnectionOutput.Append(text);
                        output = Span<char>.Empty;
                    }
                }
            };
            term.TermReady += (_, _) =>
            {
                _termReady = true;
                TryLog($"[{DateTimeOffset.Now:O}] TERM_READY{Environment.NewLine}");
                _termReadySource.TrySetResult();
            };
        }
        catch (Exception exception)
        {
            _startError = exception.ToString();
            StatusText.Text = $"No se pudo preparar la terminal: {exception.Message}";
            TryLog($"ERROR {DateTimeOffset.Now:O}{Environment.NewLine}{_startError}{Environment.NewLine}");
        }
    }

    private async void StartSession()
    {
        if (_sessionStarting || _term is null || _processFactory is null)
        {
            return;
        }

        _sessionStarting = true;
        try
        {
            var term = _term;
            var command = Terminal.StartupCommandLine;
            var processFactory = _processFactory;
            var workingDirectory = Terminal.WorkingDirectory;
            var startTask = Task.Run(() =>
            {
                try
                {
                    term.Start(
                    command,
                    logOutput: true,
                    factory: processFactory,
                workingDirectory: workingDirectory);
                }
                catch (Exception exception)
                {
                    _startError = exception.ToString();
                    _termReadySource.TrySetException(exception);
                    throw;
                }
            });
            var completed = await Task.WhenAny(_termReadySource.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            if (completed != _termReadySource.Task)
            {
                if (startTask.IsFaulted)
                {
                    await startTask;
                }

                throw new TimeoutException("La terminal no inició en 15 segundos.");
            }

            await _termReadySource.Task;
            // Connect only after TermPTY.Start has created the process. Otherwise
            // EasyTerminalControl starts that same TermPTY itself with its default factory.
            Terminal.ConPTYTerm = term;
            string bufferedOutput;
            lock (_preconnectionOutputLock)
            {
                bufferedOutput = _preconnectionOutput.ToString();
                _preconnectionOutput.Clear();
                if (bufferedOutput.Length > 0)
                {
                    term.WriteToUITerminal(bufferedOutput);
                }
                _terminalConnected = true;
            }
            TryLog($"[{DateTimeOffset.Now:O}] TERM_READY_CONTROL_CONNECTED INITIAL_OUTPUT_REPLAYED chars={bufferedOutput.Length}{Environment.NewLine}");

            _processTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _processTimer.Tick += (_, _) => UpdateProcessStatus();
            _processTimer.Start();
            StartButton.IsEnabled = false;
            ChildButton.IsEnabled = true;
            VtButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            UpdateProcessStatus();

            _ = startTask.ContinueWith(
                task => Dispatcher.Invoke(() =>
                {
                    if (task.IsFaulted)
                    {
                        _startError = task.Exception?.GetBaseException().ToString();
                        TryLog($"ERROR LECTURA {DateTimeOffset.Now:O}{Environment.NewLine}{_startError}{Environment.NewLine}");
                    }
                    UpdateProcessStatus();
                }),
                TaskScheduler.Default);
        }
        catch (Exception exception)
        {
            var details = _startError ?? exception.ToString();
            TryLog($"ERROR {DateTimeOffset.Now:O}{Environment.NewLine}{details}{Environment.NewLine}");
            _processFactory?.Dispose();
            _processFactory = null;
            if (Terminal.Parent is Border border)
            {
                border.Child = null;
            }

            StatusText.Text = $"No se inició la terminal: {exception.Message}";
        }
    }

    private void UpdateProcessStatus()
    {
        var process = _term?.Process;
        if (process is null || !_termReady)
        {
            return;
        }

        try
        {
            var hasExited = process.HasExited;
            var state = hasExited
                ? $"TERMINÓ (código {GetExitCode()})"
                : "ACTIVO";
            var pid = process is JobObjectProcessFactory.IProcessDiagnostics diagnostics
                ? $"PID {diagnostics.ProcessId}; "
                : GetPackageProcessId() is int packageProcessId
                    ? $"PID {packageProcessId}; "
                    : string.Empty;
            StatusText.Text = $"{pid}Proceso {state}; Job Object de la sesión."
                + (_startError is null ? "" : " Error de lectura/arranque: " + _startError);
            if (!_processStartedLogged && !hasExited)
            {
                _processStartedLogged = true;
                TryLog($"{Environment.NewLine}[{DateTimeOffset.Now:O}] PROCESS_ACTIVE {pid.TrimEnd(';', ' ')} command={Terminal.StartupCommandLine}{Environment.NewLine}");
            }
            if (hasExited)
            {
                StopButton.IsEnabled = false;
                if (!_processExitLogged)
                {
                    _processExitLogged = true;
                    TryLog($"[{DateTimeOffset.Now:O}] PROCESS_EXITED {pid.TrimEnd(';', ' ')} exitCode={GetExitCode()}{Environment.NewLine}");
                }
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"No se pudo consultar el proceso: {exception.Message}";
        }

        int? GetExitCode() =>
            process is JobObjectProcessFactory.IProcessDiagnostics diagnostics
                ? diagnostics.ExitCode
                : null;

        int? GetPackageProcessId() =>
            process?.GetType().GetProperty("Pid")?.GetValue(process) is int processId
                ? processId
                : null;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _term?.Process?.Kill();
            StatusText.Text = "Job Object terminado.";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void ChildButton_Click(object sender, RoutedEventArgs e)
    {
        _term?.WriteToTerm("Start-Process powershell.exe -ArgumentList '-NoProfile -Command \"Start-Sleep 120\"'\r\n");
        StatusText.Text = "Se solicitó un proceso hijo de prueba dentro de la sesión.";
    }

    private async void VtButton_Click(object sender, RoutedEventArgs e)
    {
        const string marker = "VT-Prueba-ñ-✓";
        _term?.WriteToTerm("$e=[char]27; Write-Host ($e + '[32mVT-Prueba-ñ-✓' + $e + '[0m')\r\n");
        StatusText.Text = "Enviando texto UTF-8 y una secuencia ANSI/VT...";
        await Task.Delay(1000);

        var output = _term?.ConsoleOutputLog?.ToString() ?? string.Empty;
        var ansiObserved = output.Contains("\u001b[32m", StringComparison.Ordinal);
        var textObserved = output.Contains(marker, StringComparison.Ordinal);
        StatusText.Text = ansiObserved && textObserved
            ? "ANSI/VT y UTF-8 observados en la salida ConPTY."
            : $"Prueba no concluyente (ANSI: {ansiObserved}, UTF-8: {textObserved}).";
    }

    private void TryLog(string text)
    {
        if (string.IsNullOrWhiteSpace(_logPath))
        {
            return;
        }

        try
        {
            lock (_logLock)
            {
                var logDirectory = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                File.AppendAllText(_logPath, text);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"No se pudo escribir el registro de terminal: {exception}");
        }
    }
}
