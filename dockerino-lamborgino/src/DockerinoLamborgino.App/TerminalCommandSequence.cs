using System.IO;
using System.IO.Pipes;
using System.Text;

namespace DockerinoLamborgino.App;

// Control messages travel over a random, current-user-only pipe, not the
// terminal stream. Commands remain visible only through normal terminal output.
internal sealed class TerminalCommandSequence
{
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<IReadOnlyList<int>> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _count;

    public TerminalCommandSequence(string[] commands)
    {
        _count = commands.Length;
        var name = "dockerino-" + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeServerStream(name, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        // Base64 keeps arbitrary command text out of the wrapper's syntax.
        var encoded = commands.Select(c => "'" + Convert.ToBase64String(Encoding.UTF8.GetBytes(c)) + "'");
        var script = $$"""
            $dlPipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', '{{name}}', [System.IO.Pipes.PipeDirection]::Out)
            $dlPipe.Connect(15000)
            $dlWriter = [System.IO.StreamWriter]::new($dlPipe)
            $dlWriter.AutoFlush = $true
            $dlCommands = @({{string.Join(",", encoded)}})
            for ($dlIndex = 0; $dlIndex -lt $dlCommands.Count; $dlIndex++) {
                $global:LASTEXITCODE = 0
                try {
                    Invoke-Expression ([System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($dlCommands[$dlIndex])))
                    $dlSucceeded = $?
                    $dlCode = if ($global:LASTEXITCODE -ne 0) { $global:LASTEXITCODE } elseif (-not $dlSucceeded) { 1 } else { 0 }
                } catch {
                    Write-Error -ErrorRecord $_ -ErrorAction Continue
                    $dlCode = 1
                }
                $dlWriter.WriteLine("$dlIndex $dlCode")
                if ($dlCode -ne 0) { break }
            }
            $dlWriter.Dispose()
            """;
        CommandLine = "pwsh.exe -NoLogo -NoProfile -NoExit -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        if (CommandLine.Length > 32766)
        {
            _pipe.Dispose();
            _cancellation.Dispose();
            throw new ArgumentException("The command sequence exceeds the Windows command-line limit.", nameof(commands));
        }
    }

    public string CommandLine { get; }
    public Task<IReadOnlyList<int>> Completion => _completion.Task;

    public async Task ReadResultsAsync(Func<int, int, Task> notify)
    {
        await _pipe.WaitForConnectionAsync(_cancellation.Token);
        using var reader = new StreamReader(_pipe, Encoding.UTF8);
        var results = new List<int>(_count);
        for (var index = 0; index < _count; index++)
        {
            var line = await reader.ReadLineAsync(_cancellation.Token);
            var fields = line?.Split(' ');
            if (fields is not { Length: 2 } || !int.TryParse(fields[0], out var receivedIndex)
                || receivedIndex != index || !int.TryParse(fields[1], out var code))
                throw new InvalidDataException("The command result channel closed or returned an invalid result.");
            results.Add(code);
            await notify(index, code);
            if (code != 0) break;
        }
        _completion.TrySetResult(results.AsReadOnly());
    }

    public void Fail(Exception exception) => _completion.TrySetException(exception);

    public void Stop()
    {
        _completion.TrySetCanceled();
        _cancellation.Cancel();
        _pipe.Dispose();
    }
}
