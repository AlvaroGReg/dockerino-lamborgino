# Development plan: dockerino-lamborgino

This plan turns the decisions in [decisiones.md](decisiones.md) into implementable stages. The status of the Phase 1 spike is recorded in [prototype/README.md](prototype/README.md).

## Phase 0: finalize product decisions

The initial platform, path, terminal interface, button, and branch decisions are detailed in [README.md](README.md) and [decisiones.md](decisiones.md).

- Phase 1 manual acceptance is complete (prompt, physical keyboard with marker, and visual resize), in addition to build and passive checks. Preserve the evidence and limitations before building the UI.
- Distribution decision: require the .NET 10 runtime to be installed rather than shipping a self-contained executable. Development and build instructions may require the .NET 10 SDK.

## Phase 1: integration prototype

**Status: the Phase 1 blocker was resolved and the spike acceptance criteria were approved on 2026-11-19.** The reproduced issue was isolated to the host's standard handles: with `STARTF_USESTDHANDLES` and null handles, the prompt reaches ConPTY and the shell remains alive. The regression failed before and passed after the fix; `pwsh` and `cmd` passed passive checks, and a test process's exit code 23 was observed. Debug and Release build successfully. PID, state, errors, and output are logged without treating `TermReady` as acceptance. The user confirmed the prompt, physical keyboard, and visual resize; the marker file created from the terminal was verified. See [prototype/README.md](prototype/README.md) for evidence and limitations. Phase 2 has since started; its separate acceptance is tracked below.

Create a minimal prototype with **EasyWindowsTerminalControl** behind a project-owned interface/adapter that can:

- Start PowerShell/pwsh and test commands in ConPTY.
- Render ANSI/VT, receive keyboard input, maintain encoding, and handle resize.
- Assign the root process to a Job Object and verify that stopping a session terminates only its descendants.
- Check whether the control's process factory/extension allows assignment to a Job Object before resuming the process; otherwise, create a minimal fork to integrate this lifecycle.
- Start a test Docker container from the terminal and separately test client exit/interruption and container stop.
- In the historical WEBS-CMS test case, validate `app`, `backOffice`, and main `.csproj` paths, and run the three real workspace commands in separate sessions. These are test inputs, not launcher requirements.

Test paths containing spaces and different structures, including EmbutidosEzequiel, whose application directory is `Ezequiel`.

Verify the selected control's license, versions, and dependencies. Keep alternatives documented in [README.md](README.md), but do not delay the prototype for an exhaustive comparison. Distinguish terminating the `docker compose` process from stopping or removing its containers. Use an isolated test project name for Docker Compose; never terminate the Docker daemon or stop containers globally.

**Output:** a decision on the reusable ConPTY control and evidence of complete terminal interaction, process isolation, and a scoped Docker lifecycle. Do not proceed to the full UI if an essential requirement fails.

## Phase 2: WPF skeleton

**Status: Phase 2 tests completed and the skeleton accepted by the user.**
Implementation, automated results, the user's manual observations, and
remaining evidence limitations are in
[`dockerino-lamborgino/PHASE-2-EVIDENCE.md`](dockerino-lamborgino/PHASE-2-EVIDENCE.md).

- Create an independent .NET 10 WPF application in its own workspace directory, not inside a site.
- Implement a compact, borderless, always-on-top window that can collapse and expand.
- Add a floating menu for projects, current branch, commands, and terminal sessions.
- Integrate interactive terminal views and allow sessions to be opened or reselected from the menu.
- Keep sessions alive when the menu is collapsed or closed. Closing the application stops its sessions and their owned process trees; detached Docker containers require separate explicit management.
- Create a domain/backend layer for sessions and projects shared by the UI and CLI. Every application action must be available through the CLI so an AI agent can use the same supported operations as the UI.

## Phase 3: project registration and manual launch configuration

**Status: implemented, automatically verified, and accepted by the user.**
The WPF editor and CLI use the shared configuration service. See
[Phase 3 evidence](dockerino-lamborgino/PHASE-3-EVIDENCE.md) for the checks
and their limitations. Git branch selection and execution of saved launches
remain in later phases.

- Start with an empty list of saved projects.
- Provide a **Find project** action that lets the user select one project directory. Register only that directory; do not scan its children or automatically discover projects.
- Create and edit project and launch configurations manually in the UI or through the CLI. Do not infer project structure, working directories, commands, launch types, or other configuration from files.
- Establish the CLI entry point and shared domain/backend operations. Implement CLI parity for project registration and configuration management in this phase; expose Git, launch, and terminal-session operations through the CLI in the phases that implement those features.
- The user may direct an agent to inspect project files according to supplied criteria or consult project guidance such as `AGENTS.md` and `README.md`. The agent can then submit the user-requested project details and exact launch methods through the CLI; this is agent-directed work, not application auto-detection or implicit configuration generation.
- Allow users to configure project names and paths, actual working directories, multiple named launch types, each action's chosen color, process counts, commands, ordering, and success dependencies. Support review before saving, persistence, and later editing.
- Validate paths and configuration structure, and report missing or invalid values for user resolution. Never execute commands while registering a project or saving a configuration.
- Enable Git branch operations only for valid Git repositories, and offer only actions present in the saved configuration.
- Show registration and validation errors without preventing use of other valid projects.

## Phase 4: repository and branch management

**Status: implemented; user acceptance and some verification remain outstanding.** See [Phase 4 evidence](dockerino-lamborgino/PHASE-4-EVIDENCE.md) for checks and limitations. The scope and guardrails are recorded in [D6](decisiones.md#d6-operate-git-in-explicitly-associated-repositories).

- Let users manually associate multiple repository paths with a project through the UI and CLI, including paths outside the project directory. Do not discover repositories automatically. Validate paths and preserve usability of projects without valid repositories.
- For each associated repository, query its current branch, local and remote branches, and working-tree status. Show local and remote branches in visually distinct groups, with distinct icons and a separator (visual reference: the GitHub extension in VS Code).
- Switch one repository at a time to an existing local branch using `git switch`, without requesting a merge. Selecting a remote branch creates a local tracking branch when necessary, without a separate confirmation. Do not offer general-purpose branch creation or batch branch switching.
- Provide explicit fetch and fast-forward-only pull for one or multiple selected repositories. A pull that requires merge or rebase reports the failure without starting either; its fetch may still update remote-tracking references. Continue batch operations after individual failures and report the result for each repository.
- For fetch, use the current branch's upstream remote; if there is none, use the sole remote when exactly one exists. If no remote exists or multiple remotes are ambiguous, report the problem without guessing. Apply this independently to each repository in a batch. Pull requires the current branch's configured upstream; report an error if it is missing.
- Show local changes and let Git decide whether an operation is possible. Never automatically stash, reset, resolve conflicts, or synchronize; do not offer commit or push.
- Reuse the user's Git credential mechanisms without storing credentials in the application. Support or clearly report interactive authentication and failures; verify behavior with the user's Git environment.
- Refresh displayed repository state after operations, preserve project selection, and show actionable Git errors. Expose the same operations and validation through the CLI.

**Acceptance checks:** exercise multiple manually associated repositories, including an external path and a non-Git project; switching local and remote branches with clean and changed working trees; fast-forward and divergent pulls; batch partial failures; fetch with an upstream remote, a sole remote, no remote, and multiple ambiguous remotes; pull without an upstream; and authentication success and failure. Verify UI/CLI parity and that no unrequested synchronization, merge, stash, reset, commit, or push occurs.

## Phase 5: configurable launches

- Run saved launch configurations in integrated interactive ConPTY terminals.
- Expose launch, status, and session-management operations through the CLI as well as the UI, using managed project, action, and session identifiers.
- Present user-configured, named launch actions with their chosen colors. Support multiple launch types without providing two fixed default actions or assigning fixed meanings to colors.
- Support configurable numbers of processes and sessions, independent parallel sessions, and sequential steps within a session.
- Support steps conditional on a previous step's successful exit; on failure, keep output visible, skip dependent steps, and allow independent sessions to continue.
- Run only the selected action's configured commands. Make configured descriptions and known effects visible regardless of color.
- Show progress/status, prevent duplicate clicks during the operation, and retain useful diagnostic output.
- Validate the selected configuration's working directories, commands, required tools, shell, and terminal components; identify missing components without assuming specific tools are needed by every project.

## Phase 6: visual design and application

- Produce a visual design for the application UI and review it with the user
  before implementation.
- Resolve and reflect the dashboard/navigation and empty-state questions in
  that design, including access to the saved-project list from the initial
  view and whether an empty terminal should be shown before a session exists.
- Apply the accepted design to the WPF application, keeping existing project,
  configuration, and session behavior intact.
- Verify the updated UI manually and with appropriate UI tests; record the
  design decisions and implementation evidence.

## Phase 7: hardening and delivery

- Test paths with spaces, invalid paths, incomplete projects, and terminal creation/resize/interaction failures.
- Test Docker with the daemon available and unavailable, attached and detached Compose, client interruption, stopping the test project, and volume preservation.
- Test branch switching with a clean tree and rejection/warnings with local changes.
- Test configurations with different structures and process counts, parallel sessions, sequential steps, and successful and failed prerequisites. Verify that unconfigured commands never run.
- Verify that every user-visible application action is available through the CLI with equivalent validation and behavior.
- Use the [WEBS-CMS example](examples/webs-cms.md) as a real test case, including success and failure of its optional update sequence; assess its effects before executing the update.
- Test creating, editing, saving, and launching multiple action types with different selected colors; verify labels distinguish actions independently of color.
- Document installation, execution, project registration, manual launch configuration, and CLI operations for users and AI agents.
- Verify the chosen distribution requirement (.NET 10 installed).
- Verify that Job Objects and the session registry stop only descendants of the requested session; do not accept arbitrary PIDs from the UI or CLI.

## MVP acceptance criteria

1. The app starts with no automatically discovered projects; the user can register one explicitly selected project directory and create its configuration manually.
2. Users can manually enter and edit project paths, working directories, and commands; configurations persist for later use. Registration and saving do not execute commands or infer values from project files.
3. Selecting a project shows the current branch of each explicitly associated valid repository; projects without Git remain usable without Git operations.
4. A local branch can be selected without the app discarding local changes.
5. A launch opens the configured number of integrated, interactive terminals associated with isolated sessions, using its saved commands and directories.
6. Each launch action runs only its configured commands; commands absent from that action never run implicitly.
7. Configured independent sessions can run in parallel; sequential steps execute in order, and a failed prerequisite prevents success-dependent steps without stopping independent sessions.
8. The user receives an actionable message when commands, paths, or terminal components are missing.
9. Terminal sessions remain accessible when the menu is collapsed; users can interact with them in the application and stop them individually without affecting unrelated processes.
10. Users can configure multiple named launch types and select from several colors for each; names distinguish actions independently of color. Known effects can be explained for the relevant action.
11. Local and remote branches are distinguished; selecting a remote branch without an existing local branch creates a local tracking branch without separate confirmation. Explicit fetch and fast-forward-only pull support individual and selected repositories.
12. Every application action available in the UI is available through the CLI, so an AI agent can inspect project guidance under user direction and carry out supported operations using the same application behavior and validation.

The [WEBS-CMS example](examples/webs-cms.md) is one acceptance test fixture, including its 11 observed repositories and optional three-session update workflow. It does not limit the projects, commands, or number of sessions supported by the MVP.

## Implementation questions

- Review the selected ConPTY control's full dependency and license graph before distribution.

## Future terminal improvements

- Investigate and optionally implement terminal keyboard handling and shortcuts.
  The user reports that pressing Tab removes focus from the terminal instead of
  completing input. Preserve shell and interactive-program behavior, consider
  Shift+Tab, and provide an accessible way to move focus out of the terminal.
  This issue is not yet fixed or independently diagnosed.
