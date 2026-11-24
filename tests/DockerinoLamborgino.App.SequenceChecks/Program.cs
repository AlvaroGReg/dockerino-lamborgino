using System.Diagnostics;
using System.Text.Json;
using DockerinoLamborgino.App;

// The same apphost acts as an executable CLI stub in the spawned shell.
if (Environment.GetEnvironmentVariable("DOCKERINO_TEST_CAPTURE") is { } capturePath)
{
    var invocation = new
    {
        args,
        sessionId = Environment.GetEnvironmentVariable("DOCKERINO_SESSION_ID"),
        pipeName = Environment.GetEnvironmentVariable("DOCKERINO_PIPE_NAME")
    };
    File.AppendAllText(capturePath, JsonSerializer.Serialize(invocation) + Environment.NewLine);
    return;
}

// Isolated protocol checks. These launch PowerShell directly, not through ConPTY.
await CheckAsync(["Write-Output 'first'", "Write-Output 'second'"], [0, 0], false);
await CheckAsync(["Write-Output 'first'", "& cmd.exe /c exit 7", "Write-Output 'MUST_NOT_RUN'"], [0, 7], true);
await CheckAsync(["throw 'failure'", "Write-Output 'MUST_NOT_RUN'"], [1], true);
await CheckUnexpectedClosureAsync();
await CheckStopAsync();
await CheckInteractiveResetAsync();
Console.WriteLine("Sequence checks passed.");

static Process Start(TerminalCommandSequence sequence, string? capturePath = null)
{
    var start = new ProcessStartInfo("pwsh.exe", sequence.CommandLine["pwsh.exe ".Length..])
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    if (capturePath is not null)
    {
        start.Environment["DOCKERINO_TEST_CAPTURE"] = capturePath;
        start.Environment["DOCKERINO_CLI_PATH"] = Environment.ProcessPath
            ?? throw new InvalidOperationException("Executable path unavailable.");
    }
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

static async Task CheckInteractiveResetAsync()
{
    var sessionId = Guid.NewGuid();
    var pipeName = "dockerino-reset-test-" + Guid.NewGuid().ToString("N");
    var capturePath = Path.GetTempFileName();
    var sequence = new TerminalCommandSequence(["throw 'configured failure'"], sessionId, pipeName);
    using var process = Start(sequence, capturePath);
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    try
    {
        await sequence.ReadResultsAsync((_, _) => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(20));
        var results = await sequence.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        if (!results.SequenceEqual([1])) throw new Exception("Expected the configured command to fail.");

        await process.StandardInput.WriteLineAsync("dockerino reset");
        await process.StandardInput.WriteLineAsync("dockerino projects list 'two words'");
        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var stdout = await output;
        var stderr = await errors;
        if (process.ExitCode != 0)
            throw new Exception($"PowerShell exited with {process.ExitCode}.\n{stdout}\n{stderr}");
        var lines = await File.ReadAllLinesAsync(capturePath);
        if (lines.Length != 2)
            throw new Exception($"Expected two CLI invocations, got {lines.Length}.\n{stdout}\n{stderr}");
        var expectedArguments = new[] { new[] { "reset" }, new[] { "projects", "list", "two words" } };
        for (var i = 0; i < lines.Length; i++)
        {
            using var invocation = JsonDocument.Parse(lines[i]);
            var root = invocation.RootElement;
            var receivedArguments = root.GetProperty("args").EnumerateArray().Select(arg => arg.GetString()).ToArray();
            if (!receivedArguments.SequenceEqual(expectedArguments[i]) ||
                root.GetProperty("sessionId").GetGuid() != sessionId ||
                root.GetProperty("pipeName").GetString() != pipeName)
                throw new Exception($"Unexpected CLI invocation: {lines[i]}");
        }
    }
    finally
    {
        sequence.Stop();
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        File.Delete(capturePath);
    }
}
