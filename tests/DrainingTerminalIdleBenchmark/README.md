# Draining terminal idle measurement

This standalone Windows-only diagnostic starts real `DrainingTerminal` readers and
idle `cmd.exe` processes in individually owned job objects. It waits for each
command's output marker, warms up, then measures the benchmark process's
`TotalProcessorTime` over a wall-clock interval. It disposes only its own jobs
and waits for the readers to finish. It does not attach a UI terminal, change
application configuration, or touch existing sessions.

```powershell
dotnet build tests/DrainingTerminalIdleBenchmark/DrainingTerminalIdleBenchmark.csproj -c Release
foreach ($n in 0, 10, 30, 60) {
    foreach ($r in 1..3) {
        dotnet tests/DrainingTerminalIdleBenchmark/bin/Release/net10.0-windows/DrainingTerminalIdleBenchmark.dll $n 10 3
    }
}
```

Arguments are session count (0–128), sample seconds (1–300), and warmup
seconds (1–120). Run on a quiet machine, preferably in both ascending and
descending order, and retain individual samples. A nonzero exit means setup or
cleanup failed, so the sample must not be considered valid.

`cpu_one_core_pct` is 100 × process CPU time / wall time: 100% means one
fully occupied logical CPU, not the percentage of the entire machine. Thread
counts include the harness, runtime and session management, not just readers.
The harness raises the thread-pool minimum to avoid starvation during setup;
this differs from the application. The benchmark intercepts and discards
terminal output and does not connect the UI. Thus it measures the host's reader
and session-management overhead, **not** isolated `PeekNamedPipe` cost or
end-to-end application CPU. It excludes child `cmd.exe` and pseudoconsole host
CPU, UI rendering, startup/teardown, active output, power use and responsiveness.
The 10-second samples are subject to Windows CPU accounting granularity, timer
resolution and competing workloads; no machine-wide idle condition was verified.

## Sample results

2026-11-24; Windows 10.0.26300 x64; Intel i7-10700T, 8 cores / 16
logical processors; .NET SDK 10.0.404, runtime 10.0.15; Release build.
Three fresh processes per row, 3 s warmup and 10 s sample, ascending order:

| Sessions | Host CPU % of one logical core, individual samples | Threads at sample start |
| ---: | --- | --- |
| 0 | 0.156, 0.313, 0.312 | 12, 12, 12 |
| 10 | 0.468, 1.405, 1.406 | 34, 33, 33 |
| 30 | 5.622, 0.937, 2.498 | 72, 73, 73 |
| 60 | 7.491, 13.423, 10.150 | 132, 133, 133 |

The spread is large, especially at 30 and 60 sessions. These observations show
that idle host cost can be material; they do not establish a stable per-session
cost or identify a cause. No CPU measurements were taken for child processes.

## Provisional investigation budgets

These are **proposed diagnostic thresholds**, not approved product requirements,
CI gates, or guarantees. On a quiet reference machine, compare the median of at
least five fresh 30-second runs per count, interleaving baseline and load:
investigate if baseline-subtracted host CPU exceeds 2% of one logical core at
30 idle sessions or 5% at 60 sessions. The sample medians above are 2.498% and
10.150%, compared with a 0.312% baseline; they would exceed these proposed
thresholds, but the short, non-interleaved three-run experiment does not validate
the budget protocol. Record hardware, power plan, background load, runtime,
sample spread, reader mode and UI/child-process CPU in any follow-up. Do not
extrapolate to more sessions or treat these results as a production capacity limit.
