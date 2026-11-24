using System.Diagnostics;
using DockerinoLamborgino.Terminal;

if (args.Length != 3 || !int.TryParse(args[0], out var count) || count < 0 || count > 128 ||
    !int.TryParse(args[1], out var seconds) || seconds < 1 || seconds > 300 ||
    !int.TryParse(args[2], out var warmup) || warmup < 1 || warmup > 120)
{
    Console.Error.WriteLine("Usage: DrainingTerminalIdleBenchmark <sessions 0..128> <sample seconds 1..300> <warmup seconds 1..120>");
    return 2;
}

var factories = new List<JobObjectProcessFactory>();
var runs = new List<Task>();
try
{
    ThreadPool.GetMinThreads(out var workerMin, out var ioMin);
    ThreadPool.SetMinThreads(Math.Max(workerMin, count * 2 + 8), ioMin);
    var ready = new List<Task>();
    for (var i = 0; i < count; i++)
    {
        var factory = new JobObjectProcessFactory();
        factories.Add(factory);
        var terminal = new DrainingTerminal();
        var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new System.Text.StringBuilder();
        terminal.InterceptOutputToUITerminal = (ref Span<char> text) =>
        {
            output.Append(text);
            if (output.ToString().Contains("BENCHMARK-READY")) marker.TrySetResult();
            text = Span<char>.Empty;
        };
        var command = $"\"{Path.Combine(Environment.SystemDirectory, "cmd.exe")}\" /d /q /k echo BENCHMARK-READY";
        var run = Task.Run(() => terminal.Start(command, factory: terminal.WrapFactory(factory)));
        runs.Add(run);
        ready.Add(marker.Task);
    }

    await Task.WhenAll(ready).WaitAsync(TimeSpan.FromSeconds(60));
    await Task.Delay(TimeSpan.FromSeconds(warmup));
    if (runs.Any(run => run.IsCompleted)) throw new Exception("A session exited before sampling.");
    using var process = Process.GetCurrentProcess();
    process.Refresh();
    var cpuStart = process.TotalProcessorTime;
    var threadsStart = process.Threads.Count;
    var timer = Stopwatch.StartNew();
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    timer.Stop();
    process.Refresh();
    var cpu = process.TotalProcessorTime - cpuStart;
    if (runs.Any(run => run.IsCompleted)) throw new Exception("A session exited during sampling.");
    Console.WriteLine($"sessions={count} wall_s={timer.Elapsed.TotalSeconds:F3} cpu_ms={cpu.TotalMilliseconds:F1} " +
        $"cpu_one_core_pct={100 * cpu.TotalSeconds / timer.Elapsed.TotalSeconds:F3} " +
        $"threads_start={threadsStart} threads_end={process.Threads.Count} logical_cpus={Environment.ProcessorCount}");
}
finally
{
    foreach (var factory in factories) factory.Dispose();
    try { await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(30)); }
    catch (Exception ex) { Console.Error.WriteLine($"Cleanup did not complete cleanly: {ex}"); Environment.ExitCode = 1; }
}
return Environment.ExitCode;
