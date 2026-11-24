# Analysis and decisions: dockerino-lamborgino

## 1. Context and need

Developers work with local projects whose commands, directories, process counts, and startup sequences vary. They need to save launch configurations and run them in interactive terminals. The application must support varied project structures without requiring a particular workspace layout.

The desired experience is:

1. A floating bubble on screen opens the project list.
2. The user can register one explicitly selected project directory and manually create and save its launch configuration. The list contains saved projects rather than automatically discovered projects.
3. The user selects a saved project and sees its current branch, branch selector, and applicable startup actions.
4. The user selects a named launch action, which runs its configured processes and sequence.
5. Processes appear as integrated interactive terminal sessions accessible from the floating menu.

The scope is local development. The launcher does not independently deploy, synchronize repositories, install dependencies, or change solution files. Users can explicitly request Git operations as described in D6; explicitly configured commands may have their own effects.

## 2. Feasibility

**Feasibility: high, with a clear operational dependency.**

- **Configuration:** commands, working directories, execution order, and dependencies must be stored per action rather than encoded as application-specific behavior.
- **Branches:** Git can query branches and switch the selected repository's branch without leaving the launcher.
- **Integrated terminal:** ConPTY connects shells and processes to pseudoconsoles hosted by the application. Job Objects group and stop session processes.
- **Floating UI:** WPF supports a small, borderless, always-on-top window that expands from an arrow-like control.

The main technical challenge is preserving terminal interactivity, ANSI output, resize behavior, and safe process-tree management. Configured commands may fail because of credentials, network, or dependencies and need visible error handling.

## 3. Recommended decisions

### D1. Windows desktop application, not a local web app

**Decision:** create a native local app.

A normal web page cannot freely operate on disk repositories or create local pseudoconsoles with guarantees. A desktop application can host the terminal UI and manage local processes.

### D2. WPF on .NET 10

**Decision:** create a standalone desktop application using WPF on .NET 10.

WPF directly supports the floating window and Windows/.NET integration. The integrated terminal remains a separate maintenance risk: review its dependencies, .NET 10 compatibility, licensing, and maintainability.

**Alternatives excluded from the MVP:**

- **Web/Vue:** good for web interfaces, but requires a local host with system permissions and does not itself provide the floating-window use case.
- **Electron or Tauri:** technically viable, but add a stack and packaging for a small Windows-only interface.
- **PowerShell/WPF as a script:** suitable for a quick test, but less maintainable as an application with configuration, validation, and consistent error handling.

### D3. Small window and floating session menu

**Decision:** start with a compact borderless window, anchored to a configurable corner and always visible; an arrow opens the floating menu of projects, actions, and terminal sessions. The menu can open terminals associated with projects/commands and return to existing sessions.

The menu UI and terminal views can be collapsed or switched without destroying active sessions. The application explicitly owns its process lifecycle.

### D4. Explicit project registration and manual launch configuration

**Decision:** do not populate projects automatically, infer project details, or assume a fixed workspace. A **Find project** action lets the user select exactly one project directory to register. Do not scan its children or other locations.

Project and launch configurations are created and edited explicitly by the user through the UI or CLI. Do not automatically inspect project files to infer project information, working directories, startup commands, or launch types. Users can configure multiple named launch types, including a chosen color, commands, working directories, parallel or sequential execution, and success-dependent steps. Let the user review configurations before saving, persist them for later use, and allow subsequent editing. Launch actions use saved configuration and never re-infer commands when run. Registration and saving must not execute commands.

An AI agent may inspect a project when directed by the user, following supplied criteria or project guidance such as `AGENTS.md` and `README.md`. The agent can use the CLI to create or edit the requested configuration with the values it determines. This is agent-directed inspection and explicit configuration, not application auto-detection. Git operations apply only to valid, explicitly associated repositories; a project without one remains usable. Actions are offered only when configured.

Do not scan child directories to discover projects. Store the project name, paths, and commands explicitly provided by the user or an agent acting on the user's instructions. Validate supplied paths and configuration values, and report missing or invalid information rather than inventing values.

### D5. CLI parity for application actions

**Decision:** supported project, configuration, launch, and managed terminal-session actions must also be available through the CLI so AI agents can use the application's capabilities. Git is an exception: the UI provides repository status and branch switching, while the Dockerino CLI exposes only batch fetch and fast-forward-only pull, where it adds application-specific behavior. The UI and CLI share domain/backend behavior where both expose an operation. Use managed identifiers; never accept arbitrary PIDs or run commands that are not explicitly configured.

**UI-shell exception:** interactions that control the floating launcher itself (such as moving it or exiting the application from it) do not need CLI operations. Opening or activating the dashboard window must have a CLI equivalent. Repository status and branch switching remain UI features; users can use Git CLI directly for those operations.

For launches, the CLI can start the application when it is not running. When
started this way, the dashboard is not shown; only the floating launcher is
visible. A launch request returns asynchronously with managed launch and
session identifiers rather than waiting for the processes to exit. The CLI
can list and inspect launches and sessions, inspect step results, stop an
individual session or a complete launch, and both retrieve accumulated output
and follow output live. Project creation and launch configuration remain
available through the CLI.

Sessions started through the CLI have the same ownership and lifetime as
sessions started through the UI. Exiting the application stops them and warns
the user when terminal sessions will be terminated. The CLI does not make
sessions survive application exit.

### D6. Operate Git in explicitly associated repositories

**Decision:** a project can have multiple manually associated repositories, including repositories outside the project directory. Use Git CLI in the explicitly selected repository. Show each repository's current branch and status and list local and remote branches in separate groups with distinct icons and a separator (interaction reference: the GitHub extension in VS Code). Switching is individual, not a batch operation. Choosing a remote branch can explicitly create a local tracking branch when needed; general-purpose branch creation is out of scope. The Dockerino CLI exposes batch fetch and fast-forward-only pull, but not status or branch switching; developers can use Git CLI directly for those operations.

**Guardrails:**

- Run `fetch` and `pull` only on explicit request, individually or for selected repositories. Continue a batch after an individual failure and report each result.
- For fetch, use the current branch's upstream remote. If there is no upstream remote, use the sole remote when exactly one exists; otherwise report the missing or ambiguous remote without guessing. Apply this rule independently to each repository in a batch. Pull requires a configured upstream.
- Pull only with fast-forward semantics; if integration requires a merge or rebase, report the failure without starting one. A failed pull may still update remote-tracking references during fetch.
- Do not run push, stash, commit, reset, or automatic conflict resolution. Do not request a merge when switching branches.
- Show local changes and let Git accept or reject the operation; never hide changes with an automatic stash.
- Selecting a remote branch is sufficient authorization to create its local tracking branch when needed; do not ask for separate confirmation. Report naming or tracking conflicts rather than guessing.
- Reuse the user's Git authentication mechanisms without storing credentials in the application. Handle authentication prompts and failures visibly.
- Update the displayed state after operations and show Git errors without losing project selection.

### D7. Integrated interactive terminals with ConPTY

**Decision:** the target platform is Windows and the application will have its own interactive terminal control. Start with [EasyWindowsTerminalControl](https://github.com/MitchCapper/EasyWindowsTerminalControl), encapsulated behind a project-owned interface/adapter. ConPTY connects each shell/process to a pseudoconsole; the control renders VT/ANSI output, captures keyboard input, and communicates size changes. PowerShell, `pwsh`, and other shells are hosted processes inside the terminal, not external terminals.

If the control does not meet requirements or cannot integrate the safe lifecycle, maintain a fork and adapt it there. The application must not depend directly on concrete package types outside the adapter. Pin versions and review dependencies/licenses.

The floating menu opens terminals by project/command and supports multiple sessions. A launch type may coordinate any configured number of sessions.

### D8. Session process ownership and stopping

**Decision:** each session has an internal identifier and its own process group. Use Job Objects to assign the root process and descendants from the start and stop only the group associated with the session.

Do not expose `taskkill` or a generic PID-based operation to the UI/CLI. Testing must cover descendants of `dotnet`, `npm`, and their shells, session closure, application closure, and creation failures. Safe stopping is part of the objective and requires validation in the integrated application.

**Docker requires additional management:** `docker compose` is a local client, but containers are created and maintained by the Docker daemon; they therefore do not belong to the terminal's Job Object. Distinguish interrupting/closing the client from stopping the container project. Tests use an isolated Compose project; stopping it uses an explicit operation scoped to that project (for example, `docker compose stop`/`down`, never `down --volumes` by default). Do not terminate Docker Desktop or stop unrelated containers.

### D9. Configurable launch types with explicit semantics

**Decision:** users can configure multiple named launch types per project, their commands and working directories, and a color for each action. A launch can contain different numbers of processes. Each process opens its own interactive terminal session; processes are independent and can run in parallel. Within a process, steps run in order and each waits for the previous step to finish. If a step fails, all later steps in that process are skipped, while other processes continue. Steps do not need to share shell context. A command that runs indefinitely can therefore block subsequent steps; the user is allowed to configure this and is responsible for the resulting wait. Persist these settings and run only the selected action's configured commands. Do not create two fixed default actions or assign fixed meanings to colors.

Existing saved `dependsOnSuccessOf` values remain valid when they refer to an
earlier step in the same process. No migration is required: the dependency is
redundant under the fail-fast rule, because every earlier step must succeed
before a later step runs. This also means that a failure skips later steps even
when they have no explicit dependency.

Validate the commands, paths, and tools required by the selected configuration. Keep output visible and show failures. Tests should exercise varied configurations and project structures.

**Quick launch presentation:** each action may opt into `isQuickLaunch`. The
dashboard then exposes a play button for that saved action in its project-list
row; this is only a shortcut to the same configured launch and does not change
its commands or execution semantics. The flag is optional and defaults to
`false` for existing configurations. It is editable in the UI and through the
CLI project JSON.

### D10. Explicit command effects

**Decision:** never add an update, installation, or other command implicitly. Action labels and any configured descriptions must make their purpose and known effects clear, independently of color.

### D12. Offer self-contained and framework-dependent executables

**Decision:** offer two single-file Windows application publish options: self-contained, bundling the .NET 10 runtime, and framework-dependent, which is smaller but requires the .NET 10 Windows Desktop Runtime on the target computer. Building from source requires the .NET 10 SDK; PowerShell 7 and Git remain external dependencies for their respective features.

The documented publish target is `win-x64`; other Windows architectures require publishing for their corresponding runtime identifier. Bundling the runtime increases executable size, and native libraries may be extracted to a temporary directory at startup. The two variants use separate output directories to avoid overwriting one another.

## 4. Proposed functional flow

```text
Find project → choose directory → manually configure and save
                                      ↓
Select saved project → optionally view or switch Git branch
                              ↓
                     select configured action
                              ↓
        run configured sessions, order, and dependencies
                              ↓
           show output, status, and any failed steps
```

Sessions continue when the menu is collapsed; the terminal view allows continued interaction. The application retains useful state/output and offers stopping scoped to the owned session, subject to Job Object validation.

## 5. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Incorrect or incomplete manually supplied paths and commands | Validate values, report errors, and let the user or user-directed agent correct the configuration; do not infer replacements. |
| UI and CLI behavior diverge | Route both interfaces through the same domain/backend operations and test supported actions through both. |
| Switching branches with local changes | Show status, do not use automatic stash, and preserve Git errors. |
| Incomplete ConPTY terminal (resize, ANSI, keyboard, or encoding) | Test the integrated terminal with real shells and processes; use a maintained control and document its dependencies/license. |
| Process descendants escape control | Assign early to a Job Object and test `dotnet`/`npm` trees; do not implement stop if the tree can escape. |
| Stopping the Docker client leaves containers running | Separately identify client processes and Compose projects/resources; operate only on the project started by the session and test `stop` versus `down`. |
| Failed prerequisite or slow command | Show progress and exit status; skip success-dependent steps on failure while independent sessions continue. |
| Configured commands modify project data or dependencies | Run only selected commands and make known effects visible. |
| Duplicate sessions | Identify each session, show it in the floating menu, and prevent double clicks during launch. |

## 6. Out of scope for the MVP

- Creating, cloning, or deleting repositories.
- Push, commits, stash, general-purpose branch creation, and automatic conflict resolution.
- Selecting Node/.NET versions per project.
- Installing missing dependencies unless the user explicitly configures a command that does so.
- Managing environment variables or secrets.
- External terminals as the primary UX; they can be opened manually outside the application, but are not part of the managed lifecycle.
- Cross-platform support.
- The launcher directly changing existing project configuration or scripts; explicitly configured commands may modify project files.

## 7. Conclusion

The product provides integrated interactive ConPTY terminals opened and managed from the floating menu and CLI. Project and launch configurations are entered explicitly; an AI agent may inspect project guidance at the user's direction and submit the requested configuration through the CLI. The selected approach is **WPF + .NET 10 + Git CLI + ConPTY + Job Objects**. The CLI must use managed identifiers and never terminate arbitrary PIDs.
