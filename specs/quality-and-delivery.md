# Quality, safety, and delivery specification

## 1. Platform and architecture

- **REL-001 — Target platform (Implemented).** Windows desktop application
  built with WPF on .NET 10; CLI is a .NET 10 command-line application.
- **REL-002 — Shared domain behavior (Implemented).** Core provides shared
  configuration, launch, session, and Git behavior to the UI and CLI where
  both expose the operation.
- **REL-003 — Terminal integration boundary (Implemented).** Integrated
  terminal uses EasyWindowsTerminalControl/ConPTY behind a project-owned
  adapter. Keep concrete package types behind that boundary, pin dependencies,
  and review compatibility, maintenance, and license obligations before
  distribution.
- **REL-004 — Process ownership (Implemented).** Windows Job Objects scope
  process cleanup to app-managed sessions. Never terminate arbitrary processes.

## 2. Security and side-effect boundaries

- **REL-010 — Explicit authorization.** Project registration, saving, launching,
  Git operations, and process stopping must each be initiated through the
  applicable explicit user/CLI action. Configured commands may have arbitrary
  effects; the app runs only the selected saved commands and must make known
  effects visible.
- **REL-011 — No implicit repository mutations.** Git fetch/pull and branch
  changes occur only on explicit request and follow
  [the Git spec](git-repositories.md).
- **REL-012 — Managed process controls.** Stop operations accept managed
  session/launch identifiers only. No arbitrary PID controls.
- **REL-013 — Docker daemon boundary.** A terminal Job Object does not own
  detached containers. Do not claim that stopping the client stopped
  containers; container management must be a separate explicit, project-scoped
  feature before it is added.
- **REL-014 — Credentials.** Do not persist Git credentials in the app. Git
  hooks and user-configured credential helpers are outside the app's sandbox.

## 3. Runtime requirements and packaging

- **REL-020 — Build requirement.** Building from source requires the .NET 10 SDK.
- **REL-021 — External runtime tools.** PowerShell 7 (`pwsh.exe`) is the
  default shell and Git is required for Git features. The self-contained app
  bundles .NET, not these external tools.
- **REL-022 — Publish variants.** Offer single-file Windows x64 self-contained
  and framework-dependent builds. Self-contained bundles the .NET 10 runtime;
  framework-dependent requires the .NET 10 Windows Desktop Runtime and is
  smaller. Other architectures require their own runtime identifier.
- **REL-023 — Native extraction.** Native libraries may be extracted to a
  temporary directory at startup. Publish variants use separate output
  directories.
- **REL-024 — In-memory runtime state.** Active launches, sessions, and output
  are not persisted across app restarts. Project configuration and dashboard
  ordering are persisted.
- **REL-025 — Windows installer (Partially implemented; release verification
  open).** Distribute the desktop application
  and CLI together in one Windows x64 installer, with .NET 10 included so a
  separate .NET installation is not required. Make `dockerino` available from
  a new terminal and ensure the application and CLI can locate each other.
  PowerShell 7 and Git are external dependencies, not bundled components.
  Windows ARM64 packaging is deferred.
  An initial Inno Setup installer and publish/build scripts exist; clean-machine
  and full installation/upgrade acceptance remain open in [plan.md](../plan.md).
- **REL-026 — WinGet distribution and upgrades (Planned).** Publish the
  installer through WinGet and distribute subsequent versions as WinGet
  upgrades under `AlvaroGReg.DockerinoLamborgino`, subject to repository
  acceptance. Declare `Microsoft.PowerShell` and `Git.Git` as package
  dependencies; verify detection and installation behavior. Do not rely on
  automatic updates being enabled on the user's computer.
  This remains future delivery work, outside the 1.1.0 consolidation milestone.
- **REL-027 — Upgrade safety (Partially implemented; acceptance open).** An
  upgrade replaces the installed
  application and CLI without deleting user project configuration or dashboard
  ordering. If the app is running, ask the user to close it before an interactive
  upgrade and explain that closing it stops its managed sessions, but not
  detached Docker containers. For a non-interactive upgrade, fail safely with
  an actionable result rather than closing the app or waiting for input.
  Unattended upgrades must succeed when the app is not running.
  The installer includes running-app guards and detached-container guidance;
  their integrated acceptance remains incomplete.

## 4. Verification policy

- **REL-030 — Requirement traceability.** Tests and manual checks should refer
  to the requirement IDs in these specs. The corresponding test suites and
  known gaps are listed in each feature spec and `tests/README.md`.
- **REL-031 — Separate evidence from intent.** A test existing is not proof that
  it passed in a given environment. Record user acceptance and manual test
  results only when evidence exists; otherwise leave the item open.
- **REL-032 — Cross-interface consistency.** For operations available through
  both UI and CLI, verify equivalent validation, behavior, and errors.
- **REL-033 — Real terminal integration.** Verify keyboard input, output,
  resizing, and process containment with real interactive Windows shells, in
  addition to unit tests.
- **REL-034 — Repeatable verification (Planned for 1.1.0).** Provide a documented
  entry point and Windows CI for noninteractive builds, xUnit tests, standalone
  sequence checks, and coverage collection. Preserve results with revision,
  environment, and tool versions. Treat interactive desktop and clean-machine
  packaging checks as separate explicit release gates, not implied CI passes.
- **REL-035 — Incremental output performance (Open; 1.1.0 correction).** Serve
  requested session-output suffixes without repeatedly materializing the
  consumed history or requiring dispatcher-bound history reads. Preserve
  retained output and offset semantics. Before accepting the optimization,
  define representative workloads and budgets and record comparable
  before/after allocation and latency measurements, including large histories,
  no-new-output polling, and concurrent followers. See V110-04 in the
  [consolidation plan](../plan.md#version-110-consolidation).

## 5. Out of scope

- Cross-platform support.
- Repository creation, cloning, deletion, push, commit, stash, general-purpose
  branch creation, or automatic conflict resolution.
- Automatic project/repository discovery or configuration inference.
- Per-project Node/.NET version selection, environment/secrets management, or
  implicit dependency installation.
- External terminals as the primary managed experience.
- Managing Docker Desktop or detached Docker containers as a side effect of
  stopping an application terminal.
- The application directly changing project scripts/configuration except
  through commands explicitly configured and launched by the user.

## 6. Release acceptance checklist

1. Build Debug and Release and run `dotnet test DockerinoLamborgino.sln`.
2. Run the standalone command-sequence, UI smoke, and terminal-tab integration
   checks described in `tests/README.md` on a suitable interactive Windows
   desktop.
3. Exercise paths with spaces, invalid paths, incomplete projects, storage
   conflicts, and terminal creation/resize/input failures.
4. Verify Git branch and network-operation cases in
   [the Git spec](git-repositories.md), including partial batch failure and
   authentication outcomes.
5. Verify process containment, session/launch stop scoping, and app shutdown in
   [the launch spec](launches-and-sessions.md).
6. Review the full dependency/license graph and verify both documented publish
   variants.
7. Record which checks ran, the environment, results, and remaining gaps in
   `plan.md`; do not mark a requirement accepted based only on planned tests.
8. Before WinGet distribution, test installation, CLI discovery, app/CLI
   interaction, dependency handling, upgrade with persisted configuration, and
   uninstallation on a representative clean Windows x64 system. Exercise
   upgrade while the app is running and unattended installation and upgrade.

For 1.1.0, the existing installer must pass those installation and upgrade
checks even though publishing a WinGet package is deferred. The
[milestone acceptance ledger](../plan.md#release-acceptance-ledger) tracks the
open gates, including output performance and repeatable verification.
