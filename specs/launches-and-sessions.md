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
  missing prerequisites clearly; do not install them implicitly. Selected
  process directories are checked at launch, and a missing `pwsh.exe` produces
  an actionable diagnostic when process creation reports it unavailable. Other
  prerequisite checks and real missing-shell UI verification remain open.

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
- **RUN-014 — Session visibility (Implemented; desktop smoke verified).** Sessions remain selectable
  and their output inspectable after exit/failure/stop. Closing a session tab
  stops that session; adding a terminal creates a new managed session.
  Closing a tab removes its presentation, not the retained session's CLI
  inspection/output contract. Output history is retained separately from visible
  tabs; see V110-03 in the [1.1.0 plan](../plan.md#version-110-consolidation).
- **RUN-015 — In-memory lifetime (Implemented).** Launches, sessions, and
  terminal output are held in memory for the app lifetime. Exiting the app
  stops managed sessions; they do not survive restart.
- **RUN-016 — Docker boundary (Implemented/documented).** Stopping a terminal
  stops its managed client process tree, not detached containers owned by the
  Docker daemon. Docker container cleanup is not implicit.
- **RUN-017 — Terminal status indicators (Implemented).** A terminal tab shows a
  loading indicator while its terminal is starting, and an error indicator
  when that session has failed. The terminal indicator represents that
  session's own state; loading ends when terminal startup completes, not when
  configured commands finish. Sessions opened with **New terminal** have no
  project association. Loading and error are also distinguished by color
  (amber and red respectively); the indicator has an accessible tooltip.
- **RUN-018 — Configured command failure indicators (Implemented).** For
  sessions created by configured launches, a nonzero command exit code also
  shows an error indicator on that session, its project, and the floating
  launcher. This launch-command failure remains distinct from terminal state:
  an interactive shell may remain usable after its command fails. Sessions
  opened with **New terminal** are not monitored for command failures. A
  keyboard press or mouse-button press directly in the affected terminal
  acknowledges its configured-command error indicator without changing recorded
  launch results; selecting a tab or interacting with another terminal does
  not. Actual terminal failure remains visible, and other sessions' errors
  continue to contribute to project and launcher indicators. Interactive
  commands are not monitored for subsequent failures.
- **RUN-034 — Close all terminal sessions (Planned).** Provide a control beside
  the add-terminal control that closes all currently open terminal tabs and
  stops their managed sessions. If one or more sessions are still running and
  will be interrupted, ask for confirmation before stopping any of them.
  Confirming stops all targeted sessions; cancelling leaves all sessions and
  tabs unchanged. Do not target processes outside those managed sessions, and
  do not imply that detached Docker containers are stopped.
- **RUN-019 — Reset a configured terminal (Implemented; integrated concurrency
  and delivery verification open).** A user can request
  `dockerino reset` from a terminal created by a configured launch to close
  that terminal and start a new terminal for that configured process. Run the
  complete original sequence again, in order and with the same working
  directory. Only that session is replaced; other processes in the launch
  continue unaffected. The replacement starts without the old session's error
   indicator and reports any new failure independently. An external caller may
   target a configured session explicitly as described in the CLI contract.

### Reset: motivation and implementation approach

Direct interaction acknowledges the configured-command failure indicator but
does not rerun the launch or change its recorded result. Interactive commands
are not monitored by the configured-command result protocol. Reset provides an
explicit way to replay the original sequence without interpreting terminal
text or attempting to monitor arbitrary interactive commands.

The implementation approach is:

1. Expose the CLI in the PowerShell context of a configured terminal and pass
   the managed session identity and pipe name through its environment. The
   explicit CLI form targets a managed session ID. Both forms send a request to
   the owning application, never an arbitrary process ID. The CLI must be
   available as described in the [README](../README.md#launches-and-terminal-sessions).
2. Have the application stop and close the targeted session using its
   existing scoped ownership, then create a new managed session and terminal
   tab for that process. A new shell means the old shell state and visible
   output are not retained in the replacement tab. Detached Docker containers
   are outside this stop operation.
3. Replay the original launch snapshot for that process, including every
   configured step and the working directory, rather than rerunning other
   processes or silently adopting later configuration changes. Preserve
   ordered, fail-fast execution.
4. Record the replacement execution and results in the shared backend so UI
   and CLI inspection agree. Its status indicators reflect the replacement's
   state; other sessions can still keep their own project or launcher error
   indicators active.

The replacement has a new session ID and takes the original process's place in
launch inspection. An unassociated **New terminal** does not supply the session
context required by the shorthand and cannot itself be reset.

<a id="reset-request-and-outcome-contract-open"></a>

#### Reset request and outcome contract

Reservation, asynchronous acceptance, replay, and inspection outcomes are
implemented. This contract is not fully release-verified: deterministic
acceptance-before-completion, failed response delivery, and integrated
stop/tab-close races remain open. Existing test runs and qualified user
reports are recorded in [plan.md](../plan.md); they do not establish acceptance
of every case below. The legacy anchor above is retained for existing links.

- `dockerino reset` uses `DOCKERINO_SESSION_ID` and the configured pipe for the
  current terminal. `dockerino session reset <session-id>` explicitly targets a
  configured launch session, including from outside that terminal. The explicit
  form does not require the environment session ID. Reject invalid, unknown,
  unassociated or otherwise ineligible targets without scheduling work. Reset
  connects only to an already running application; it does not start one.
- Validate and reserve an eligible request before returning a JSON acceptance
  response with the launch ID and original session ID. Exit code 0 means
  accepted, not completed; invalid syntax returns 2, and rejection or
  communication failure returns 1. Use the existing CLI output and error
  conventions. The application attempts to write and flush acceptance before
  beginning the stop; if delivery fails after reservation, the accepted work
  still runs and the caller must inspect state rather than assume rejection.
  A terminal resetting itself closes its own CLI process, so even a successfully
  sent response may not be visible and no exit code is guaranteed there.
- Launch inspection exposes a reset outcome correlated with the original
  session ID: pending, succeeded with the replacement session ID, or failed
  with a diagnostic. Pending includes the interval after acceptance and before
  replacement startup completes. A successful replacement means its terminal
  started, not that its configured commands succeeded. Keep the latest outcome
  inspectable for the application lifetime, even if stopping the original or
  starting the replacement fails. UI and CLI inspection use the same backend.
- Concurrent requests for the same original session while its reset is pending
  share one operation and acceptance; they must not create multiple
  replacements. A replacement whose startup is still pending is ineligible for
  a further reset until that operation completes. Once it completes, the old ID cannot initiate another reset
  if it has been replaced. A failed reset can be retried only if the original
  session is still the current eligible session; otherwise target the current
  session if eligible. An explicit session or launch stop while reset is pending
  cancels replacement; closing the affected tab counts as an explicit stop.
  Report cancellation as a failed outcome with a diagnostic. Do not report
  success when stop or startup fails.
- The pipe is scoped to the current Windows user, not to a particular terminal.
  The environment ID is context, not authentication: another process running as
  the same user can provide an ID. A reset affects only its specified managed
  session and never unrelated sessions.

## 3. CLI contract

- **RUN-020 — CLI parity (Implemented).** Supported project/configuration,
  launch, and managed-session operations are available from the CLI and use the
  same application backend where applicable.
- **RUN-021 — Start request (Implemented).** `launch start <project-id>
  <action-id>` returns asynchronously with managed launch/session identifiers;
  it does not wait for all processes to finish.
- **RUN-022 — Inspection and control (Implemented; closed-tab desktop smoke verified).** CLI commands list,
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
session reset <session-id>
reset
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
11. Verify a terminal tab shows loading only during terminal startup, shows
    error after that session fails, and does not inherit another session's
    state.
12. After a configured command fails, run `dockerino reset` in its terminal.
    Verify the old session closes, a new tab reruns the full original process
    sequence, and unrelated terminals remain active. Check that the old alert
    clears, a repeated failure raises a new alert, and UI and CLI inspection
    report the replacement consistently. Exercise stop/start failures and
    repeated reset requests. From an external CLI, verify acceptance precedes
    completion and inspect pending, success and failure outcomes. Verify both
    reset forms, invalid and unassociated targets, a missing application, and
    an explicit stop or tab close racing with reset. Verify a self-reset does
    not depend on its response being displayed.
13. After configured commands fail in two terminals, click or type in only one
    terminal. Verify its command-error indicator clears while the other
    terminal and aggregate indicators remain in error. Verify tab selection
    alone does not acknowledge the error, the recorded launch result remains
    failed, and subsequent interactive failures are not monitored. Repeat with
    keyboard and mouse input.
14. **RUN-014 / RUN-015 / RUN-022:** retrieve output and start a follower for
    both an independent terminal and a configured session, then close their
    tabs. Verify retained session inspection and output remain accessible,
    consumed offsets remain valid, and followers receive final output and
    completion without an artificial history reset.
15. **RUN-034:** close all terminal tabs when no session is running and verify
    they all close. Repeat with running sessions; verify cancelling changes
    nothing and confirming stops and closes every targeted session without
    affecting unrelated processes or detached Docker containers.

## 6. Verification references and gaps

Core orchestration and registry tests are in
`tests/DockerinoLamborgino.Core.Tests/Launch/` and
`tests/DockerinoLamborgino.Core.Tests/Sessions/`. Process isolation tests are
in `tests/DockerinoLamborgino.Terminal.Tests/`; command-sequence checks are in
`tests/DockerinoLamborgino.App.SequenceChecks/`. Interactive terminal checks are
in `tests/TerminalTabIntegration/` and `tests/UiSmoke/`. The real-application
self-reset and external-client integration check is
`tests/UiSmoke/ResetIntegration.ps1`; see the test README for its scope.

Automated tests do not by themselves establish that every real-world process
tree is safely contained. The release plan tracks remaining manual and
environment-dependent checks.
