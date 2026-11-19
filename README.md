# Local project launcher

## Purpose

Assess the feasibility of a small Windows desktop application for selecting a configured project, optionally changing its Git branch, and running its configured commands in integrated terminals. This document prepares development; it does not describe an implementation that already exists.

## Overview

Build a local Windows desktop application for managing development projects, switching Git branches, and opening terminals **integrated into the application** from a floating menu. Terminals will be interactive: users can see output and type into them, not merely observe logs.

The user explicitly searches for projects and chooses where to look. The launcher analyzes the selected location and generates a saved, editable launch configuration with applicable commands and working directories. Users configure launch types, their processes, ordering and dependencies, and choose a color for each action; there are no two fixed default actions. The [WEBS-CMS workflow](examples/webs-cms.md) is a concrete example and test case, not the product's required structure. The Phase 1 spike validated **WPF on .NET 10 + ConPTY + Job Objects**, including physical keyboard input and visual resizing.

### Project location

There is no prepopulated project list or automatic workspace scan. A **Find projects** button lets the user choose a project directory or a directory containing projects. The launcher analyzes only that chosen location, presents the results, and saves selected projects as launch configurations. `../../00-WEBS-CMS`, relative to this documentation directory, is an example location for testing, not a hard-coded or required workspace.

## Before implementation

The initial product decisions have been made and incorporated into the development plan. The spike and its runtime tests of ConPTY, Job Objects, real commands, and Docker are documented in [prototype/README.md](prototype/README.md). Phase 1 acceptance was completed; see [plan.md](plan.md).

## Decisions

- **Platform/terminal:** Windows. The application will have integrated, interactive ConPTY terminals; PowerShell and other shells run inside them, rather than external windows replacing the application's terminal.
- **Location and configuration:** the user initiates project search and chooses the location. Detected projects are not added until selected; the launcher generates a saved, editable configuration containing their paths and startup commands.
- **Actions:** users configure launch types, commands, working directories, execution order and conditions, and choose each action's color from several options. Actions are identified by name as well as color; there are no fixed green or orange defaults. Only explicitly configured commands run.
- **Branches:** show local and remote branches in separate sections, with distinct icons and a separator (inspired by the GitHub extension in VS Code). Choosing a remote branch requires an explicit action to create or switch to a local branch.
- **Sessions and processes:** terminals are opened and managed from the floating menu. The spike confirmed ConPTY and Job Objects for stopping a session's own process tree; the interface must never accept arbitrary PIDs.
- **Product reference:** the [CodeDeck](../CodeDeck) clone is at `../CodeDeck`, relative to this documentation. It is a reference for workflows, UI, and project, command, and launch-set concepts; it is neither the product's direct basis nor a project to modify here.
- **Docker:** the spike confirmed that closing the client session does not stop a detached container; stopping or removing containers is an explicit operation scoped to the Compose project. Containers are managed by the Docker daemon and are not ordinary descendants of the local process; Job Objects alone cannot stop them.

### Initially selected terminal control

- **Initial choice:** [EasyWindowsTerminalControl](https://github.com/MitchCapper/EasyWindowsTerminalControl), encapsulated behind a project-owned terminal adapter/interface so it can be replaced.
- **Contingency:** if the control does not meet requirements or cannot integrate process management, create and maintain a project fork.
- [WPF UI Terminal](https://github.com/emako/wpfui.terminal) is a port of Easy, not an independent implementation. [WindowsTerminalControl](https://github.com/0x5bfa/WindowsTerminalControl) and [Term1809](https://github.com/zac15987/Term1809) remain comparison references, not selected dependencies.
- **WebView2 + xterm.js + ConPTY bridge** is a last-resort alternative if the WPF Windows Terminal control approach proves infeasible.

Keep the control behind a project interface. Pin versions and review licenses and dependencies. If changes are needed, maintain an explicit fork with its own tests rather than spreading control-specific changes throughout the application.

## Executive summary

The idea is feasible as a local desktop application. It requires a real terminal integration (ConPTY), management of owned process sessions, access to multiple local repositories, and Git execution. CodeDeck will be studied as a UX and workflow reference; the spike validated the proposed **.NET 10 WPF + ConPTY + Job Objects** integration. See the [Phase 1 report](prototype/README.md).

Configurations must support different numbers and kinds of processes, sequential or parallel execution, and steps conditional on a previous step's result. They must not assume a particular project layout or technology. The [WEBS-CMS example](examples/webs-cms.md) records one real workflow used to exercise those capabilities.

## Documents

- [Architecture decisions](decisiones.md): proposal, alternatives, and risks.
- [Development plan](plan.md): phases, acceptance criteria, and outstanding questions.
- [WEBS-CMS example](examples/webs-cms.md): observed project structure and representative launch configurations for testing.

## Basis for the analysis

The proposal was checked against a real workspace described in the [WEBS-CMS example](examples/webs-cms.md). Those observations must be checked again when testing search and configuration generation. The initial inventory did not execute startup or update commands; subsequent startup tests are recorded in the spike report.
