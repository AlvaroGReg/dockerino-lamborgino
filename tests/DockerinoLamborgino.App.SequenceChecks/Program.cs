using System.Diagnostics;
using DockerinoLamborgino.App;

// Isolated protocol checks. These launch PowerShell directly, not through ConPTY.
await CheckAsync(["Write-Output 'first'", "Write-Output 'second'"], [0, 0], false);
await CheckAsync(["Write-Output 'first'", "& cmd.exe /c exit 7", "Write-Output 'MUST_NOT_RUN'"], [0, 7], true);
await CheckAsync(["throw 'failure'", "Write-Output 'MUST_NOT_RUN'"], [1], true);
await CheckUnexpectedClosureAsync();
await CheckStopAsync();
Console.WriteLine("Sequence checks passed.");

static Process Start(TerminalCommandSequence sequence)
{
    var start = new ProcessStartInfo("pwsh.exe", sequence.CommandLine["pwsh.exe ".Length..])
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    return Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
}

static async Task CheckAsync(string[] commands, int[] expected, bool checkSkipped)
{
    var sequence = new TerminalCommandSequence(commands);
    using var process = Start(sequence);
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    var notifications = new List<(int, int)>();
    try
    {
        await sequence.ReadResultsAsync((index, code) =>
        {
            notifications.Add((index, code));
            return Task.CompletedTask;
        }).WaitAsync(TimeSpan.FromSeconds(20));
        var results = await sequence.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        if (!results.SequenceEqual(expected) ||
            !notifications.SequenceEqual(expected.Select((code, index) => (index, code))))
            throw new Exception("Unexpected sequence results or notifications.");

        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var text = await output;
        await errors;
        if (checkSkipped && text.Contains("MUST_NOT_RUN", StringComparison.Ordinal))
            throw new Exception("A command after the failed step ran.");
    }
    finally
    {
        sequence.Stop();
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }
}

static async Task CheckUnexpectedClosureAsync()
{
    var sequence = new TerminalCommandSequence(["exit 0"]);
    using var process = Start(sequence);
    try
    {
        try
        {
            await sequence.ReadResultsAsync((_, _) => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(20));
            throw new Exception("A missing result was accepted as completion.");
        }
        catch (InvalidDataException) { }
    }
    finally
    {
        sequence.Stop();
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }
}

static async Task CheckStopAsync()
{
    var sequence = new TerminalCommandSequence(["Start-Sleep -Seconds 30"]);
    using var process = Start(sequence);
    try
    {
        var reading = sequence.ReadResultsAsync((_, _) => Task.CompletedTask);
        sequence.Stop();
        try
        {
            await reading.WaitAsync(TimeSpan.FromSeconds(5));
            throw new Exception("Stopped reader unexpectedly completed.");
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        if (!sequence.Completion.IsCanceled) throw new Exception("Stopped sequence was not canceled.");
    }
    finally
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }
}
