# Launches and terminal sessions specification

**Baseline:** launch orchestration and managed session operations are
implemented. Terminal integration is Windows/ConPTY-specific. Some lifecycle
and UI acceptance evidence remains environment-dependent; see
[plan.md](../plan.md).

## 1. Launch contract

- **RUN-001 — Explicit action selection (Implemented).** Run only the selected
  project's saved action and its configured commands. Never add implicit
  setup, update, install, or cleanup commands.
- **RUN-002 — Parallel processes (Implemented).** Each configured process gets
  a separate interactive terminal session. Processes are independent and may
  run concurrently.
- **RUN-003 — Ordered steps (Implemented).** Within one process, run steps in
  array order. A later step starts only after the previous step succeeds.
  Existing valid `dependsOnSuccessOf` references remain accepted; under this
  fail-fast rule they do not relax the requirement that all preceding steps
  succeed.
- **RUN-004 — Failure isolation (Implemented).** If a step fails, retain its
  output/result, skip every later step in that process, and allow independent
  processes to continue.
- **RUN-005 — Long-running steps (Implemented).** A command may run
  indefinitely. It blocks later steps in its process; this consequence is
  disclosed as a configuration responsibility, not silently bypassed.
- **RUN-006 — Diagnostics and progress (Implemented).** Show useful status,
  output, and failure information. Prevent duplicate launch activation while a
  launch request is being handled.
- **RUN-007 — Launch preflight (Open).** Before starting, validate the selected
  action's working directories, commands, shell, terminal components, and
  required tools without assuming every project needs the same tools. Identify
  missing prerequisites clearly; do not install them implicitly.

## 2. Terminal and session behavior

- **RUN-010 — Interactive terminal (Implemented).** Each session hosts a shell
  in an integrated Windows pseudoconsole (ConPTY). The terminal accepts
  keyboard input, renders terminal output, and responds to resizing.
- **RUN-011 — Default shell (Implemented).** New terminal sessions use
  PowerShell 7 (`pwsh.exe`) by default. Configured process commands are run in
  their own terminal context; steps are not required to share shell state.
- **RUN-012 — Managed identity (Implemented).** Every session and launch is
  addressed by application-managed identifiers. UI/CLI must not accept an
  arbitrary PID as a stop target.
- **RUN-013 — Scoped stop (Implemented; integration safety remains a release
  concern).** Stopping a session targets only the process tree owned by that
  session. A launch can be stopped as a managed group of its sessions.
- **RUN-014 — Session visibility (Implemented).** Sessions remain selectable
  and their output inspectable after exit/failure/stop. Closing a session tab
  stops that session; adding a terminal creates a new managed session.
- **RUN-015 — In-memory lifetime (Implemented).** Launches, sessions, and
  terminal output are held in memory for the app lifetime. Exiting the app
  stops managed sessions; they do not survive restart.
- **RUN-016 — Docker boundary (Implemented/documented).** Stopping a terminal
  stops its managed client process tree, not detached containers owned by the
  Docker daemon. Docker container cleanup is not implicit.

## 3. CLI contract

- **RUN-020 — CLI parity (Implemented).** Supported project/configuration,
  launch, and managed-session operations are available from the CLI and use the
  same application backend where applicable.
- **RUN-021 — Start request (Implemented).** `launch start <project-id>
  <action-id>` returns asynchronously with managed launch/session identifiers;
  it does not wait for all processes to finish.
- **RUN-022 — Inspection and control (Implemented).** CLI commands list,
  inspect, and stop launches; list, inspect, stop, retrieve accumulated output
  from, and follow output for sessions. Launch inspection exposes step results.
- **RUN-023 — App activation (Implemented).** CLI can open/activate the
  dashboard. If the application must be started for an operation, CLI startup
  does not show the dashboard unless the requested operation is to open it;
  normal launch startup leaves the floating launcher visible.
- **RUN-024 — Shared ownership (Implemented).** CLI-started and UI-started
  sessions have the same app ownership and shutdown behavior.
- **RUN-025 — Local IPC (Implemented).** CLI communicates with the app over a
  current-user named pipe. `DOCKERINO_PIPE_NAME` can override the pipe name in
  both processes. `DOCKERINO_APP_PATH` can specify the app executable path when
  it is not next to the CLI.

The supported command forms are:

```text
launch start <project-id> <action-id>
launch list
launch inspect <launch-id>
launch stop <launch-id>
session list
session inspect <session-id>
session output <session-id>
session follow <session-id>
session stop <session-id>
dashboard open
projects refresh [project-id]
```

## 4. Safety and failure requirements

- **RUN-030 — Early process containment (Implemented; acceptance evidence
  required).** Attach the root process and descendants to a per-session Windows
  Job Object from process creation so a child cannot escape before assignment.
- **RUN-031 — Creation and stop failures (Implemented).** Report failed starts
  and stops with diagnostics; a failed stop can be retried where the session
  registry permits it.
- **RUN-032 — No arbitrary termination (Implemented).** Do not expose generic
  `taskkill`, arbitrary process IDs, or system-wide termination operations.
- **RUN-033 — Visible configured effects (Implemented).** Action labels and
  descriptions should make purpose and known effects understandable, regardless
  of color. Never hide or imply effects based on color.

## 5. Acceptance scenarios

1. Launch an action with multiple processes and prove their sessions are
   independent and can make progress concurrently.
2. Configure sequential steps; verify order, successful continuation, and that
   a failing step skips later steps only in its process.
3. Configure an indefinitely running step and verify later steps do not start
   while independent processes remain usable.
4. Verify output is available while running and after exit, and step outcomes
   are visible through the UI and CLI.
5. Stop one session in a launch and verify unrelated sessions remain active;
   stop a launch and verify its sessions stop without affecting unrelated
   launches.
6. Verify application exit warns when sessions will be stopped and stops
   app-owned process trees regardless of whether started from UI or CLI.
7. Exercise descendants of representative shells/tools (including `dotnet` and
   `npm`) and terminal creation failure. Confirm no unrelated process is
   terminated.
8. Verify CLI launch returns before command completion, reports managed IDs,
   supports output follow, and does not show the dashboard on ordinary
   background startup.
9. Run a Docker client command and verify closing its terminal does not claim
   to stop detached containers.
10. Verify missing directories, shell, terminal components, and selected-action
    tools produce actionable errors without triggering installation.

## 6. Verification references and gaps

Core orchestration and registry tests are in
`tests/DockerinoLamborgino.Core.Tests/Launch/` and
`tests/DockerinoLamborgino.Core.Tests/Sessions/`. Process isolation tests are
in `tests/DockerinoLamborgino.Terminal.Tests/`; command-sequence checks are in
`tests/DockerinoLamborgino.App.SequenceChecks/`. Interactive terminal checks are
in `tests/TerminalTabIntegration/` and `tests/UiSmoke/`.

Automated tests do not by themselves establish that every real-world process
tree is safely contained. The release plan tracks remaining manual and
environment-dependent checks.
