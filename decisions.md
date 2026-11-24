# Decision context: Dockerino Lamborgino

This file preserves the rationale and alternatives behind notable choices. The
normative product behavior is in the [specifications](specs/README.md); this
file is supporting context and must not be used as a substitute for them.

## D1. Native Windows desktop application

**Decision:** build a native local application rather than a local web app.

**Why:** a normal web page cannot freely manage local repositories or host
pseudoconsole sessions. A desktop application can own the terminal UI and local
process lifecycle.

## D2. WPF on .NET 10

**Decision:** use WPF on .NET 10.

**Why:** it provides Windows integration and supports the floating launcher
without adding a separate web stack.

**Alternatives considered:** Web/Vue would require a privileged local host and
would not itself provide the floating-window experience; Electron or Tauri add
stack and packaging for a small Windows-only interface; a PowerShell/WPF script
is less maintainable for configuration, validation, and error handling.

## D3. Floating launcher and standard dashboard

**Decision:** keep a small persistent floating launcher separate from the
ordinary dashboard window.

**Why:** users can return to the dashboard without forcing the dashboard itself
to remain above other applications. The initial always-on-top window described
in early planning was a skeleton, not the final interaction model; see
[desktop experience](specs/desktop-experience.md).

## D4. Explicit project registration and configuration

**Decision:** do not discover projects or infer their commands/configuration.
Users explicitly register projects and save launch actions.

**Why:** project layouts and startup needs vary, and inferred commands can have
unwanted side effects. Explicit configuration keeps registration safe and makes
the launched commands reviewable. A user-directed agent may inspect project
guidance, but only to fulfill an explicit user request.

## D5. Shared UI/CLI capabilities, with a Git exception

**Decision:** expose supported project, configuration, launch, and managed
session operations through both UI and CLI, using shared backend behavior.
Git status and branch switching remain UI features; Dockerino CLI exposes batch
fetch and fast-forward-only pull.

**Why:** the CLI enables automation and agent use without duplicating business
rules. The narrower Git CLI surface is limited to batch operations where the
application adds behavior; other Git work remains available through Git
itself.

## D6. Explicitly associated repositories and conservative Git operations

**Decision:** associate repository paths manually, including paths outside a
project. Fetch and pull are explicit; pull is fast-forward-only. Do not
automatically stash, merge, rebase, reset, push, commit, or resolve conflicts.

**Why:** a project may use multiple repositories and arbitrary discovery could
operate on the wrong one. Conservative Git behavior avoids concealing local
changes or making integration decisions on the user's behalf. Selecting a
remote branch explicitly authorizes creating its local tracking branch.

## D7. Integrated ConPTY terminal behind an adapter

**Decision:** host interactive Windows terminals through ConPTY, initially
using EasyWindowsTerminalControl behind a project-owned adapter.

**Why:** integrated terminals keep command interaction and output in the
launcher while ConPTY provides shell interactivity, keyboard input, and resize
support. The adapter limits coupling to a third-party control and permits
replacement or a maintained fork if necessary.

## D8. Per-session process ownership

**Decision:** use a Windows Job Object per managed session and address stop
operations by managed session/launch IDs, never arbitrary PIDs.

**Why:** stopping must affect only processes owned by the requested session.
Detached Docker containers are owned by the Docker daemon, not the terminal's
Job Object, so stopping a terminal must not be represented as stopping those
containers.

## D9. Configurable launches with explicit execution semantics

**Decision:** allow user-defined named actions with configurable processes,
ordered steps, colors, and optional quick-launch presentation.

**Why:** projects need different startup sequences and process counts. Multiple
independent terminals permit parallel work; sequential fail-fast steps preserve
the configured order without implying that commands share shell state. Colors
are presentation only.

## D10. No implicit command effects

**Decision:** run only commands saved in the explicitly selected action; do not
implicitly install, update, or modify project state.

**Why:** configured commands can have significant effects. The user should be
able to identify an action's purpose and known effects before running it.

## D12. Two single-file publish variants

**Decision:** document self-contained and framework-dependent Windows builds.

**Why:** self-contained deployment avoids requiring a .NET runtime on the
target machine, while framework-dependent deployment is smaller for users who
already have the Windows Desktop Runtime. PowerShell 7 and Git remain external
requirements for their features.

## D13. Current-user Windows sign-in startup

**Decision:** offer an application-level option to start the app at sign-in for
the current Windows user; do not automatically select a project or launch an
action.

**Why:** startup preference belongs to the application, not an individual
project. Application options are accessible at the bottom of the dashboard's
project-list column, independently of the selected project.

## D14. Asynchronous scoped terminal reset

**Decision:** accept reset requests asynchronously, with a terminal-context
shorthand and an explicit managed-session-ID CLI form. Report the eventual
outcome through launch inspection rather than the initiating CLI response.

**Why:** self-reset closes the invoking terminal and its CLI process; external
callers still need confirmation that the request was accepted. Acceptance
cannot imply the replacement started successfully. The current-user pipe is a
local trust boundary, not authentication of an individual terminal. See
[RUN-019](specs/launches-and-sessions.md#reset-request-and-outcome-contract)
for the contract and implementation status.

## D15. Windows installer and WinGet distribution

**Decision:** distribute the app and CLI together in a Windows x64 installer
with .NET included. Treat PowerShell 7 and Git as external dependencies and
use WinGet for installation and upgrades. Defer Windows ARM64 packaging.

**Why:** one installer makes the desktop application and global CLI available
together without requiring users to install .NET separately. WinGet provides a
Windows-oriented distribution and update path. The existing source publish
variants in D12 remain useful independently of the planned installer.

**Initial implementation choices:** use Inno Setup with a per-user installation;
publish versioned installers as GitHub releases in the project repository. Use
GitHub Actions to build and produce release artifacts; initially submit WinGet
manifest updates manually. Use the package identifier
`AlvaroGReg.DockerinoLamborgino`, subject to acceptance. Do not use a code-signing
certificate. If the app is running during an interactive upgrade, ask the user
to close it; a non-interactive upgrade must fail safely instead of closing it.
An in-app updater that could restart the app is a possible future enhancement,
not part of the current delivery.

**Dependency research:** the WinGet manifest schema supports dependencies by
package ID from the same source; `Microsoft.PowerShell` and `Git.Git` are the
intended IDs. This does not establish that every existing or portable
installation will be recognized, or that either executable is on the app's
PATH. Test actual detection, versions, scope and command availability before
release. The installer used directly does not automatically get WinGet's
dependency processing. See the [WinGet manifest schema](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.28.0/installer.md)
and [install documentation](https://learn.microsoft.com/en-us/windows/package-manager/winget/install).
See
[quality and delivery](specs/quality-and-delivery.md) for requirements and
[the plan](plan.md) for outstanding work.

## D16. Consolidation before feature growth

**Decision (2026-11-24):** dedicate version 1.1.0 to stabilizing the existing
application, measuring and improving output performance, repeatable
verification, and hardening delivery. Retain the current platform and product
boundaries. WinGet remains the intended future distribution channel in D15,
but introducing it is outside this consolidation milestone.

**Why:** the review identified cross-resource validation coupling, stale Git
refreshes, and output access tied to visible terminal views. Targeted changes
to ownership and testable boundaries address these risks without the cost and
regression surface of a rewrite. A different UI framework, wholesale MVVM
migration, or database would not by itself fix them.

Separate retained session history from presentation and support incremental
output reads. Do not silently truncate retained output or change its offsets.
An implementation detail or retention-policy change beyond the existing
contract needs an explicit specification update. The implementation retains
session output separately from visible tabs; verification status is tracked
in the plan.

See the [1.1.0 plan](plan.md#version-110-consolidation) for findings, sequencing,
evidence, and release gates rather than duplicating the backlog here.

## Historical numbering

The decision identifiers are retained for continuity with earlier planning
references. The missing D11 is intentional; no D11 decision is recorded in the
source decision history.
