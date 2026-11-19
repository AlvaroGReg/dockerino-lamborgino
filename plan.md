# Development plan: local project launcher

This plan turns the decisions in [decisiones.md](decisiones.md) into implementable stages. The status of the Phase 1 spike is recorded in [prototype/README.md](prototype/README.md).

## Phase 0: finalize product decisions

The initial platform, path, terminal interface, button, and branch decisions are detailed in [README.md](README.md) and [decisiones.md](decisiones.md).

- Phase 1 manual acceptance is complete (prompt, physical keyboard with marker, and visual resize), in addition to build and passive checks. Preserve the evidence and limitations before building the UI.
- Distribution decision: require .NET 10 to be installed rather than shipping a self-contained executable.

## Phase 1: integration prototype

**Status: the Phase 1 blocker was resolved and the spike acceptance criteria were approved on 2026-11-19.** The reproduced issue was isolated to the host's standard handles: with `STARTF_USESTDHANDLES` and null handles, the prompt reaches ConPTY and the shell remains alive. The regression failed before and passed after the fix; `pwsh` and `cmd` passed passive checks, and a test process's exit code 23 was observed. Debug and Release build successfully. PID, state, errors, and output are logged without treating `TermReady` as acceptance. The user confirmed the prompt, physical keyboard, and visual resize; the marker file created from the terminal was verified. See [prototype/README.md](prototype/README.md) for evidence and limitations. The full UI has not been started.

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

- Create an independent .NET 10 WPF application in its own workspace directory, not inside a site.
- Implement a compact, borderless, always-on-top window that can collapse and expand.
- Add a floating menu for projects, current branch, commands, and terminal sessions.
- Integrate interactive terminal views and allow sessions to be opened or reselected from the menu.
- Keep sessions alive when the menu is collapsed or closed, and define behavior when the application closes.
- Create a domain/backend layer for sessions and projects that can later be shared by the UI and an AI-facing CLI.

## Phase 3: user-initiated project search and configuration

- Start with an empty list of saved projects; do not scan locations automatically.
- Provide a **Find projects** button and let the user select either a project directory or a parent containing projects.
- Analyze only the chosen location and, for a parent directory, its immediate children. Exclude hidden and package directories from candidates.
- Present candidates and allow the user to choose which projects to configure.
- Analyze candidates for recognizable project information and propose applicable commands where identifiable, without requiring a particular stack or project layout.
- Generate a configuration per selected project with its name, project path, actual working directories, and identifiable startup commands. Allow users to configure multiple named launch types, choose each action's color, and edit process counts, commands, working directories, ordering, and success dependencies. Allow review and editing before saving, persist it, and allow later edits.
- Report missing or ambiguous paths and commands for user resolution; do not silently invent values or execute commands during search or saving.
- Allow manual configuration for unrecognized structures; never generate unsupported commands.
- Enable Git branch operations only for valid Git repositories, and offer only actions present in the saved configuration.
- Show search and validation errors without preventing use of other valid projects.

## Phase 4: branch selector

- Query the current branch, local and remote branches, and working-tree status.
- Show local and remote branches in visually distinct groups, with distinct icons and a separator (visual reference: the GitHub extension in VS Code).
- Allow switching to a local branch using `git switch`.
- Do not automatically stash/reset or synchronize with a remote.
- Treat selecting a remote branch as an explicit action to create or switch to a local tracking branch; do not implicitly check out a remote branch.
- Refresh the UI after switching and display Git errors.

## Phase 5: configurable launches

- Run saved launch configurations in integrated interactive ConPTY terminals.
- Present user-configured, named launch actions with their chosen colors. Support multiple launch types without providing two fixed default actions or assigning fixed meanings to colors.
- Support configurable numbers of processes and sessions, independent parallel sessions, and sequential steps within a session.
- Support steps conditional on a previous step's successful exit; on failure, keep output visible, skip dependent steps, and allow independent sessions to continue.
- Run only the selected action's configured commands. Make configured descriptions and known effects visible regardless of color.
- Show progress/status, prevent duplicate clicks during the operation, and retain useful diagnostic output.
- Validate the selected configuration's working directories, commands, required tools, shell, and terminal components; identify missing components without assuming specific tools are needed by every project.

## Phase 6: hardening and delivery

- Test paths with spaces, invalid paths, incomplete projects, and terminal creation/resize/interaction failures.
- Test Docker with the daemon available and unavailable, attached and detached Compose, client interruption, stopping the test project, and volume preservation.
- Test branch switching with a clean tree and rejection/warnings with local changes.
- Test configurations with different structures and process counts, parallel sessions, sequential steps, and successful and failed prerequisites. Verify that unconfigured commands never run.
- Use the [WEBS-CMS example](examples/webs-cms.md) as a real test case, including success and failure of its optional update sequence; assess its effects before executing the update.
- Test creating, editing, saving, and launching multiple action types with different selected colors; verify labels distinguish actions independently of color.
- Document installation, execution, user-initiated project search, and launch configuration.
- Verify the chosen distribution requirement (.NET 10 installed).
- Verify that Job Objects and the session registry stop only descendants of the requested session; do not accept arbitrary PIDs from the UI or CLI.

## MVP acceptance criteria

1. The app starts with no automatically discovered projects; the user can search a chosen location, select candidates, and save their configurations without including auxiliary directories or common packages.
2. Generated paths work even when the inner directory differs from the repository name; users can review and edit paths and commands, and saved configurations persist for later use. Search and saving do not execute commands.
3. Selecting a Git-backed project shows its current branch; projects without Git remain usable without branch operations.
4. A local branch can be selected without the app discarding local changes.
5. A launch opens the configured number of integrated, interactive terminals associated with isolated sessions, using its saved commands and directories.
6. Each launch action runs only its configured commands; commands absent from that action never run implicitly.
7. Configured independent sessions can run in parallel; sequential steps execute in order, and a failed prerequisite prevents success-dependent steps without stopping independent sessions.
8. The user receives an actionable message when commands, paths, or terminal components are missing.
9. Terminal sessions remain accessible when the menu is collapsed; users can interact with them in the application and stop them individually without affecting unrelated processes.
10. Users can configure multiple named launch types and select from several colors for each; names distinguish actions independently of color. Known effects can be explained for the relevant action.
11. Local and remote branches are distinguished; selecting a remote requires an explicit local-branch creation action.

The [WEBS-CMS example](examples/webs-cms.md) is one acceptance test fixture, including its 11 observed repositories and optional three-session update workflow. It does not limit the projects, commands, or number of sessions supported by the MVP.

## Implementation questions

- What happens to sessions when the application closes and reopens: persist, reconnect, or terminate?
- Review the selected ConPTY control's full dependency and license graph before distribution.
