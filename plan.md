# Development plan: dockerino-lamborgino

This plan turns the decisions in [decisions.md](decisions.md) into implementable stages.

## Phase 0: finalize product decisions

The initial platform, path, terminal interface, button, and branch decisions are detailed in [README.md](README.md) and [decisions.md](decisions.md).

- Phase 1 manual acceptance is complete (prompt, physical keyboard with marker, and visual resize), in addition to build and passive checks.
- Distribution decision: require the .NET 10 runtime to be installed rather than shipping a self-contained executable. Development and build instructions may require the .NET 10 SDK.

## Phase 1: terminal integration

**Status: completed and accepted by the user on 2026-11-19.** The user confirmed a working ConPTY prompt, physical keyboard input, and resizing; a marker file created from the terminal was verified. The application and its tests now live in the root `src/` and `tests/` directories.

## Phase 2: WPF skeleton

**Status: Phase 2 tests completed and the skeleton accepted by the user.**

- Create an independent .NET 10 WPF application in its own workspace directory, not inside a site.
- Implement a compact, borderless, always-on-top window that can collapse and expand.
- Add a floating menu for projects, current branch, commands, and terminal sessions.
- Integrate interactive terminal views and allow sessions to be opened or reselected from the menu.
- Keep sessions alive when the menu is collapsed or closed. Closing the application stops its sessions and their owned process trees; detached Docker containers require separate explicit management.
- Create a domain/backend layer for sessions and projects shared by the UI and CLI. Supported project, configuration, Git, launch, and terminal-session operations must be available through the CLI. Floating-launcher interactions are UI-only, except opening or activating the dashboard, which must have a CLI equivalent; see [D5](decisions.md#d5-cli-parity-for-application-actions).

## Phase 3: project registration and manual launch configuration

**Status: implemented, automatically verified, and accepted by the user.**
The WPF editor and CLI use the shared configuration service. Git branch
selection and execution of saved launches remain in later phases.

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

**Status: implemented; user acceptance and some verification remain outstanding.** The scope and guardrails are recorded in [D6](decisions.md#d6-operate-git-in-explicitly-associated-repositories).

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

**Status: completed and accepted by the user on 2026-11-21.** The user reports
that Phase 5 tests have been completed successfully; individual test setups
and results were not provided.

- Run saved launch configurations in integrated interactive ConPTY terminals.
- Expose launch, status, and session-management operations through the CLI as well as the UI, using managed project, action, and session identifiers.
- The CLI can start the application if it is not running, without showing the dashboard; only the floating launcher is visible. Launch requests return asynchronously with managed launch and session identifiers, without waiting for process completion.
- Provide CLI operations to list and inspect launches and sessions, inspect step results, stop a session or a complete launch, retrieve accumulated terminal output, and follow output live. Retain CLI project creation and launch configuration operations.
- CLI-started sessions share the UI-started sessions' application lifetime. On application exit, warn when terminal sessions will be terminated and stop them regardless of how they were started.
- Present user-configured, named launch actions with their chosen colors. Support multiple launch types without providing two fixed default actions or assigning fixed meanings to colors.
- Each configured process opens its own interactive terminal session. Processes are independent and can run in parallel; steps within a process run sequentially, each waiting for the previous step to finish. Steps do not need to share shell context.
- If a step fails, keep output visible and skip all later steps in that process; independent processes continue. Permit indefinitely running steps even though they block later steps in the same process; the user is responsible for that configuration.
- Adapt the existing optional `dependsOnSuccessOf` configuration model to these semantics; decide how to handle existing saved configurations before implementation.
- Run only the selected action's configured commands. Make configured descriptions and known effects visible regardless of color.
- Show progress/status, prevent duplicate clicks during the operation, and retain useful diagnostic output.
- Validate the selected configuration's working directories, commands, required tools, shell, and terminal components; identify missing components without assuming specific tools are needed by every project.

The floating-launcher and dashboard behavior above is a target for CLI startup;
the current Phase 2 skeleton is not the final launcher design. Phase 6 still
covers the visual design and application of the full launcher interaction.

## Phase 6: visual design and application

**Status: the latest requested navigation and terminal-tab revision is
implemented. The user reported positive results for the requested manual
checks; explicit design acceptance has not been separately stated.**

- **Preliminary UI direction from the user; not yet a finalized design or
  implementation specification:**
  - Keep a small semicircular launcher attached to a screen edge and always
    on top. Hovering expands it slightly, exposing an application-exit `X` and
    enough area to click or drag.
  - Dragging the launcher moves it along the screen edge. Clicking it opens or
    brings the dashboard window to the foreground. Use standard Windows
    pointer behavior to distinguish a click from a drag.
  - The launcher can be moved between monitors and placed on either side of
    the boundary between screens. Remember its position between application
    sessions. Its default position is the top center of the system's primary
    display. Support all four edges of every monitor, placing the launcher
    within the usable work area so it does not overlap the taskbar.
  - The dashboard window behaves like a standard application window: it is not
    always on top and may be covered, minimized, or hidden when another
    application receives focus. The launcher remains visible and can bring the
    dashboard back.
  - Closing the dashboard window does not exit the application or stop its
    terminal sessions. Exiting the application is available from the launcher,
    not the dashboard window's close button, and terminates the terminal
    sessions. Warn the user before exiting when terminal sessions will be
    terminated; a small confirmation dialog offering cancel or confirm is
    sufficient.
  - Do not design touch interactions. The floating launcher itself (including
    its placement and exit interaction) does not need CLI operations. Opening
    or activating the dashboard does need a CLI equivalent to clicking the
    launcher.
  - This is a UI direction for Phase 6 only. The current Phase 2 skeleton's
    always-on-top window behavior must not be treated as the final interaction
    model. Preserve existing project, configuration, and session behavior
    unless a separate decision changes it.
- Touch interaction is out of scope.
- Produce a visual design for the application UI and review it with the user
  before implementation.
- Resolve and reflect the dashboard/navigation and empty-state questions in
  that design, including access to the saved-project list from the initial
  view and whether an empty terminal should be shown before a session exists.
- Apply the accepted design to the WPF application, keeping existing project,
  configuration, and session behavior intact. The latest requested proposal
  uses a project-list column and a selected-project tools column; the terminal
  panel appears only after a launch or opening a new terminal, with closable
  session tabs and a final add-tab button.
- Verify the updated UI manually and with appropriate UI tests; record the
  design decisions and implementation evidence.

## Phase 7: hardening and delivery

- Test paths with spaces, invalid paths, incomplete projects, and terminal creation/resize/interaction failures.
- Test Docker with the daemon available and unavailable, attached and detached Compose, client interruption, stopping the test project, and volume preservation.
- Test branch switching with a clean tree and rejection/warnings with local changes.
- Test configurations with different structures and process counts, parallel sessions, sequential steps, and successful and failed steps. Verify that unconfigured commands never run.
- Verify CLI parity for supported project, configuration, Git, launch, and terminal-session operations, and verify that opening or activating the dashboard has a CLI equivalent. Floating-launcher placement and application exit remain UI-only.
- Test configurations with both successful and failing steps, and verify that configured command effects are visible and only requested commands run.
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
7. Configured processes have their own terminal sessions and can run in parallel; steps execute sequentially, and a failed step skips all later steps in its process without stopping independent processes.
8. The user receives an actionable message when commands, paths, or terminal components are missing.
9. Terminal sessions remain accessible when the menu is collapsed; users can interact with them in the application and stop them individually without affecting unrelated processes.
10. Users can configure multiple named launch types and select from several colors for each; names distinguish actions independently of color. Known effects can be explained for the relevant action.
11. Local and remote branches are distinguished; selecting a remote branch without an existing local branch creates a local tracking branch without separate confirmation. Explicit fetch and fast-forward-only pull support individual and selected repositories.
12. Supported project, configuration, Git, launch, and terminal-session operations are available through the CLI with equivalent behavior and validation. Opening or activating the dashboard also has a CLI equivalent; floating-launcher placement and application exit are UI-only.

## Implementation questions

- Review the selected ConPTY control's full dependency and license graph before distribution.

## Future terminal improvements

- Investigate and optionally implement terminal keyboard handling and shortcuts.
  The user reports that pressing Tab removes focus from the terminal instead of
  completing input. Preserve shell and interactive-program behavior, consider
  Shift+Tab, and provide an accessible way to move focus out of the terminal.
  This issue is not yet fixed or independently diagnosed.
