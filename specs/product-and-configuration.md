# Product and project configuration specification

**Baseline:** core project registration/configuration is implemented. UI and CLI
use the shared configuration service. See `tests/README.md` for current
automated and interactive verification.

## 1. Purpose and boundaries

Dockerino Lamborgino is a local Windows desktop launcher for development
projects. A user explicitly registers projects, configures named launch actions,
and starts those saved actions in managed interactive terminals.

The application is not a deployment service or project-discovery tool. It must
not discover projects, infer commands from project files, install dependencies,
or execute commands as a side effect of registration, editing, validation, or
saving. An agent may inspect files only when the user directs it to do so; the
agent's resulting configuration must still be explicitly reviewed/saved.

## 2. Actors and entry points

- A user configures projects and actions through the WPF application or CLI.
- Both interfaces use the shared Core configuration behavior.
- A user-directed AI agent may use the CLI to perform explicitly requested
  configuration work.
- The default project store is
  `%LOCALAPPDATA%\DockerinoLamborgino\projects`. The app accepts
  `--project-storage <absolute-directory>` for an isolated store.

## 3. Requirements

### Registration and discovery

- **CFG-001 — Explicit registration (Implemented).** Start with an empty project
  store when none exists. Register only the one directory selected/provided by
  the user. Do not scan child directories, search other locations, or infer
  project metadata.
- **CFG-002 — No implicit execution (Implemented).** Registration, validation,
  review, and saving must not run configured commands or otherwise execute
  project code.
- **CFG-003 — Manual configuration (Implemented).** Project names, paths,
  repositories, actions, working directories, and commands come from explicit
  user input or user-directed agent work. Missing values must be reported, not
  invented.
- **CFG-004 — Agent inspection boundary (Implemented as a policy).** A user may
  direct an agent to inspect project guidance (for example `AGENTS.md` or
  `README.md`) and submit requested values. This is not application
  auto-detection and does not authorize unrequested commands.

### Project and action data

- **CFG-010 — Project identity (Implemented).** A project has a unique GUID
  `id`, a name, an existing absolute `directory`, an optional list of distinct
  absolute repository paths, and an `actions` array.
- **CFG-011 — Action identity and presentation (Implemented).** An action has a
  unique GUID, name, `#RRGGBB` color, optional description, optional
  `isQuickLaunch` (default `false`), and one or more configured processes.
  Color is presentation only; it does not imply action semantics.
- **CFG-012 — Process and step structure (Implemented).** Each process has a
  unique GUID, name, existing absolute `workingDirectory`, and one or more
  steps. Each step has a unique GUID and a command. `dependsOnSuccessOf`, when
  present, must refer to an earlier step in the same process.
- **CFG-013 — Empty action list (Implemented).** A project may be saved without
  actions. The UI offers only actions that have been configured.
- **CFG-014 — Strict JSON (Implemented).** Unknown JSON properties are rejected.
  IDs must be unique in their applicable scope; callers creating JSON must
  generate IDs explicitly.
- **CFG-015 — Quick launch flag (Implemented).** `isQuickLaunch` is optional,
  defaults to `false` for existing configurations, and only adds a dashboard
  shortcut to the same saved action. It does not change the action's commands
  or execution semantics.

### Editing, validation, and persistence

- **CFG-020 — Review before save (Implemented in UI).** The user can review
  project configuration before confirming a save and can edit saved
  configuration later.
- **CFG-021 — Validation (Implemented).** Validate required values, path
  syntax/existence, repository associations, and configuration structure.
  Report actionable validation/storage errors without silently repairing or
  inventing values.
- **CFG-022 — Concurrent-edit protection (Implemented).** `get`/`list` return
  a revision with configuration. Updating an existing project requires the
  expected current revision. Reject stale revisions and require reload before
  another edit. Saving without a revision is create-only.
- **CFG-023 — Corrupt/conflicting data isolation (Implemented).** A bad project
  configuration or conflicting edit must be reported without preventing
  unrelated valid projects from being used.
- **CFG-024 — Display order separation (Implemented).** Dashboard ordering is
  persisted separately from project configuration and its revision.
- **CFG-025 — Operation-scoped availability (Implemented; integrated verification open).** Keep
  structurally valid saved projects visible with resource diagnostics when an
  associated repository or a process working directory becomes unavailable.
  Validate availability for the selected operation; an unavailable resource
  used only by another action or by Git must not block a valid selected launch.
  This does not remove configuration/save validation in `CFG-021` or permit
  launching with missing required resources. Loading preserves availability
  diagnostics and the selected launch rechecks its own process directories; see V110-01 in the
  [consolidation plan](../plan.md#version-110-consolidation).

### CLI configuration interface

- **CFG-030 — Shared behavior (Implemented).** CLI and UI configuration
  operations use the same configuration service and validation rules.
- **CFG-031 — Commands (Implemented).** The CLI supports `register`, `list`,
  `get`, `validate`, and `save`. `register` creates an empty action list.
  `get`/`list` return project data, validation issues, and revision information.
- **CFG-032 — Exit codes (Implemented).** Exit code `0` indicates success, `1`
  an operation error, and `2` invalid CLI syntax.
- **CFG-033 — App refresh (Implemented).** After CLI configuration changes,
  users can request `projects refresh [project-id]`. The CLI may start the app
  with only the floating launcher visible if the app is not running.
- **CFG-034 — CLI help (Implemented).** `--help` as the sole argument prints the
  supported command syntax to standard output and exits with code `0`, without
  starting the app or changing project data. Invalid syntax remains an error.
- **CFG-035 — CLI version (Implemented).** `--version` as the sole argument prints
  the CLI assembly version to standard output and exits with code `0`, without
  starting the app or changing project data.

## 4. Acceptance scenarios

1. With no stored projects, opening the app shows no discovered projects.
   Registering a selected directory adds only that project; nested directories
   remain undiscovered.
2. Register, validate, review, and save a configuration containing a marker
   command; verify that the marker is not created until the action is explicitly
   launched.
3. Save a project with no actions and verify it remains selectable and editable.
4. Reject malformed/unknown JSON properties, duplicate IDs, invalid paths, and
   invalid step references with actionable issues.
5. Load a revision, save a change, then attempt another save with the old
   revision; verify the stale update is rejected and valid unrelated projects
   remain accessible.
6. Verify omitted `isQuickLaunch` behaves as `false`; setting it changes only
   dashboard presentation.
7. Verify project registration/configuration from UI and CLI has equivalent
   validation and persistence behavior.
8. **CFG-025 / GIT-002 / RUN-007:** save a valid project, then make an external
   repository or another action's working directory unavailable. Verify the
   project remains visible with diagnostics and an unaffected action still
   launches. Verify the affected operation fails with an actionable error.

## 5. Verification references and gaps

Automated coverage is in
`tests/DockerinoLamborgino.Core.Tests/Configuration/`. Interactive persistence
and no-command-on-save checks are in
`tests/UiSmoke/ProjectConfigurationPersistenceUiSmoke.ps1`.
CLI command syntax is covered in
`tests/DockerinoLamborgino.Core.Tests/Launch/LaunchCliSyntaxTests.cs` and
configuration behavior should remain covered as the CLI evolves.

Agent inspection is a workflow policy rather than an app feature. Exact
coverage of every malformed project layout is not claimed by these references.
