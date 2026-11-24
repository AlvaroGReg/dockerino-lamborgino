# Session output microbenchmark

Run `dotnet run -c Release --project tests/SessionOutputBenchmark`.
The before case reproduces the previous locked `StringBuilder.ToString()` and
suffix slicing; the after case calls `SessionOutputHistory.Read`. Both use the
same 4,194,304 UTF-16-unit history and offsets. The concurrent case runs four
readers; numbers are elapsed wall time divided by total reads, not per-request
latency percentiles. Allocation is process-wide and includes harness overhead.
This does not measure named pipes, JSON, UI responsiveness, or tail latency.

Provisional in-process budgets: less than 64 KiB allocated per read and less
than 5 ms elapsed per read for these workloads, including concurrent readers.
These are engineering guardrails, not a product-wide service-level objective.

2026-11-24, base revision `26a05dd` with uncommitted changes, Windows
10.0.26300, Intel i7-10700T, .NET 10.0.15, Release, 40 reads per follower:

| Workload | Before ms/read | Before B/read | After ms/read | After B/read |
|---|---:|---:|---:|---:|
| No new output | 2.864 | 8,389,582 | 0.004 | 123 |
| 1 Ki UTF-16-unit suffix | 2.500 | 8,390,885 | 0.010 | 2,169 |
| Four followers, no new output | 2.586 | 8,388,782 | 0.001 | 79 |

All after measurements meet these provisional budgets on this machine. Repeat
under controlled load and measure end-to-end behavior before release acceptance.

A second local run on the same date and environment measured 1.676 → 0.002
ms/read and 8,389,581 → 119 B/read for empty polling; 1.380 → 0.002 ms/read and
8,390,883 → 2,173 B/read for the 1 Ki suffix; and 1.586 → 0.001 ms/read and
8,388,794 → 79 B/read for four empty-polling followers. This is another
in-process run, not independent end-to-end acceptance.
