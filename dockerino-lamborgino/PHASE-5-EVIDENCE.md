# Phase 5 evidence (in progress)

Phase 5 has not been accepted. The following checks were run on Windows on
2026-11-20 against the working tree:

- `dotnet test DockerinoLamborgino.sln -c Release --no-restore`: 39 core and
  one terminal isolation test passed.
- `dotnet run --project tests/DockerinoLamborgino.App.SequenceChecks`:
  PowerShell sequence protocol checks passed. These run PowerShell directly,
  not through the integrated terminal.
- An isolated Release application and CLI, using a temporary project store and
  named pipe, launched a configured PowerShell command through ConPTY. The
  CLI returned managed IDs, reported a zero step exit code, retrieved its
  output and stopped the launch.
- A second isolated launch had two processes. One returned exit code 7 and
  skipped its next step; the independent process returned zero and produced
  its expected output.

These checks did not verify physical keyboard interaction, visual rendering,
resize, live output following, application exit confirmation, shell/tool
validation, CLI auto-start, or all UI/CLI parity requirements. They do not
establish Phase 5 acceptance. The first attempted isolated test connected to
an already running application using the shared default pipe and did not
execute a launch; subsequent checks used a separate pipe. A Debug rebuild was
blocked by that pre-existing application's executable lock; it was not closed.
