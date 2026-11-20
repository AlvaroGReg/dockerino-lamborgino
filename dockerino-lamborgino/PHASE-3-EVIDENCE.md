# Phase 3 evidence — 2026-11-20

Project registration and configuration use a shared service from the WPF editor
and the CLI. Projects are entered explicitly; configuration is not inferred
and saving does not run the configured commands. The editor uses JSON for the
actions array. Git operations and execution of saved actions remain future work.

## User acceptance

On 2026-11-20, the user reported that the Phase 3 manual tests were completed
successfully. The user initially reported a persistence problem, then clarified
that it was a user error. The report did not separately enumerate the results
of each individual manual check.

## Automated checks

The following were run on Windows with .NET SDK 10.0.404:

```powershell
dotnet build DockerinoLamborgino.sln -c Debug
dotnet build DockerinoLamborgino.sln -c Release
dotnet test DockerinoLamborgino.sln -c Debug
dotnet test DockerinoLamborgino.sln -c Release
pwsh -NoProfile -File .\Smoke-Window.ps1
pwsh -NoProfile -File .\Smoke-Phase3.ps1
```

The builds passed without warnings or errors. Each test configuration passed
22 Core/CLI tests and one ConPTY isolation test. The Phase 2 window smoke test
passed. The Phase 3 UI Automation test used an explicitly isolated temporary
store and verified creation, review, saving, editing, restart persistence,
display of an action, rejection of a stale revision, and continued access to a
valid project alongside a corrupt file. A command that would create a marker
was not executed. An independent CLI check reproduced and verified fixes for
invalid colors ending in a newline and the exit code for invalid projects.

## Limits

The UI Automation test enters an explicit directory directly; it does not
exercise the operating system folder picker. Visual acceptance is based on the
user's report above, not on that automated test.
CLI tests call its entry point in-process with an isolated store rather than
launching a separate CLI process. Concurrent writes are tested with multiple
threads, not separate processes; the mutex and revision protocol does not
protect against external editors that ignore it. The tests do not verify
launching saved commands or Git operations, which are not implemented yet.
Phase 2 visual acceptance and its separate limitations are recorded in
[PHASE-2-EVIDENCE.md](PHASE-2-EVIDENCE.md).
