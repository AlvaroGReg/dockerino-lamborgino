<p align="center">
  <img src="assets/dockerino-lamborgino-icon.svg" alt="Dockerino Lamborgino icon" width="128">
</p>

# Dockerino Lamborgino

Dockerino Lamborgino is a Windows desktop launcher for local development
projects. Register a project, define named launch actions, and run their
configured commands in interactive terminals hosted by the application. It
also provides explicit Git branch and synchronization operations for
repositories associated with a project.

The application does not discover projects, inspect files to infer commands,
install dependencies, or run commands during registration. Users configure
projects and launch actions through the UI or CLI; only the selected action's
saved commands are run.

## What it does

- Register and edit projects and launch actions through the desktop UI or CLI.
- Associate one or more Git repositories with a project, including repositories
  outside its directory. View status and branches, switch branches, and
  explicitly fetch or fast-forward pull.
- Run named actions in interactive terminal sessions. Each configured process
  has its own terminal; steps within a process run in order, while independent
  processes can run concurrently.
- Inspect launch/session state and output, and stop an application-managed
  session or launch through the UI or CLI.
- Keep terminal sessions visible and manageable from the application dashboard.

This is a local Windows development tool, not a deployment service. Sessions
and their output are held in memory for the application lifetime. Stopping a
terminal does not stop detached Docker containers.

## How it is built

- **Desktop UI:** WPF on .NET 10.
- **CLI:** a .NET 10 command-line application.
- **Shared behavior:** a Core library provides domain and configuration
  behavior used by the UI and CLI.
- **Terminals:** EasyWindowsTerminalControl hosts interactive Windows
  pseudoconsole (ConPTY) sessions. Job Objects scope process cleanup to
  application-managed sessions.
- **Git:** repository operations use the Git command-line client.

## Requirements

To build from source, install:

- .NET 10 SDK.

The self-contained published executable bundles the .NET 10 runtime. The
lighter framework-dependent executable requires the .NET 10 Windows Desktop
Runtime to be installed on the target computer. Both variants still require:

- PowerShell 7 (`pwsh.exe`), the default shell for new terminal sessions.
- Git for repository features.

## Build and run

Run these commands from the repository root:

```powershell
dotnet build DockerinoLamborgino.sln -c Debug
dotnet build DockerinoLamborgino.sln -c Release
dotnet test DockerinoLamborgino.sln
dotnet run --project .\src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj
```

## Publish a single-file executable

From the repository root, choose one of these Windows x64 options.

### Self-contained (no .NET runtime installation)

```powershell
dotnet publish .\src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\artifacts\publish\win-x64-self-contained
```

Output: `artifacts\publish\win-x64-self-contained\DockerinoLamborgino.App.exe`.
The .NET 10 runtime is bundled, so it can run without .NET installed, but the
executable is larger. Native libraries may be extracted to a temporary
directory when the application starts.

### Framework-dependent (smaller; requires .NET 10)

```powershell
dotnet publish .\src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\artifacts\publish\win-x64-framework-dependent
```

Output: `artifacts\publish\win-x64-framework-dependent\DockerinoLamborgino.App.exe`.
This executable is smaller because it does not bundle the .NET runtime. The
target computer must have the .NET 10 Windows Desktop Runtime installed.

Both commands target x64; publish separately with the appropriate runtime
identifier for other Windows architectures. PowerShell 7 and Git remain
external requirements for their respective features in both variants.

## Register and configure a project in the UI

Choose **Add project** and select one directory, or select a saved project and
open **Project instructions & configuration…**. Enter the project name and directory, then use the
**Repositories** and **Actions** tabs to add explicit repositories and launch
actions. Each action contains one or more processes, each with ordered command
steps. A project can also be saved without actions. Select **Review and save…**
to inspect the values before confirming. Select a saved project and
**Edit selected** to change it later.
Validation and storage errors appear in the window; a conflicting edit must be
reloaded.

Choose associated repository roots explicitly; these may be outside the project
directory. Existing projects without associations remain empty. Actions are
displayed by name, color value, and description. For
isolated testing, the application accepts
`--project-storage <absolute-directory>` to override its default project store.

In the dashboard, drag the handle next to a project or launch to reorder it.
The order is saved separately from project configurations and restored on
restart. Launch order is specific to each project; reordering does not run an
action or change its commands. Project-list rows show the project name and drag
handle without the directory path. Actions marked **Lanzado rápido** in their
configuration also show a play button in the project row, allowing that saved
action to start without first selecting the project and opening its tools.

Select **Application options** at the bottom of the project list, alongside
**New terminal** and **Add project**, to open application-level settings from
any dashboard view. The **Start Dockerino Lamborgino with Windows** option
starts the application when the current Windows user signs in. It does not
select a project or run any configured actions or processes.

## Configure projects with the CLI

The CLI and application use the shared project configuration service. Projects
are stored individually under
`%LOCALAPPDATA%\DockerinoLamborgino\projects` by default. An absent store starts
empty. Registration selects only the directory given; it does not scan for
projects, infer commands, or run commands.

Register a project and save its editable project object to a JSON file:

```powershell
$cli = @("run", "--project", ".\src\DockerinoLamborgino.Cli", "--")
$result = dotnet @cli register "Example" "C:\My Projects\Example" | ConvertFrom-Json
$projectId = $result.project.id
$revision = $result.revision
$projectFile = ".\project-edit.json"
$result.project | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $projectFile
dotnet @cli list
dotnet @cli get $projectId
# Edit project-edit.json, then validate and save.
dotnet @cli validate $projectFile
dotnet @cli save $projectFile $revision
```

`register` creates an empty action list. `get` and `list` return JSON with the
project, validation issues, and a revision. Edit the `project` object in a
separate JSON file, then validate and save it with the current revision. A
stale revision is rejected; reload before editing again. `save` without a
revision is create-only. Exit codes are 0 for success, 1 for an operation error,
and 2 for invalid CLI syntax.

Each project has a unique GUID `id`, a name, an existing absolute `directory`,
an optional array of distinct absolute repository paths, and an `actions`
array. Each action has a unique GUID, name, `#RRGGBB` color, optional
description, an optional `isQuickLaunch` boolean, and one or more processes.
Set `isQuickLaunch` to `true` to show a play button for that action beside the
project name in the dashboard; omitted values default to `false`. Each process
has a unique GUID, name,
existing absolute `workingDirectory`, and one or more steps. Each step has a
unique GUID and command, and may refer to an earlier step with
`dependsOnSuccessOf`. Steps run in array order; a failure skips later steps in
that process while independent processes continue. Unknown JSON properties are
rejected. Generate unique IDs explicitly, for example with
`[guid]::NewGuid()` in PowerShell.

After creating or saving a project through the CLI, refresh the project list in
the application. If the app is not running, the CLI starts it with only the
floating launcher visible:

```powershell
dotnet @cli projects refresh [project-id]
dotnet @cli dashboard open
```

## Launches and terminal sessions

Select a project in the dashboard and click one of its configured launch
actions. Each configured process uses a separate PowerShell 7 terminal. Session
tabs can be selected or closed; closing a tab stops that session, and `+` opens
a new terminal.
For configured launches, a nonzero command exit marks the associated terminal,
project, and floating launcher as having an error; standalone terminals are not
included in command-result monitoring. Clicking or pressing a key directly in
the affected terminal clears its configured-command error indicator without
changing the recorded result. Later interactive commands are not monitored for
failures; another session's error or an actual terminal failure remains visible.

The CLI communicates with the running application over a current-user named
pipe. If needed, it starts the application with the dashboard hidden. Set
`DOCKERINO_APP_PATH` to the absolute application executable path if it is not
beside the CLI executable. For isolated instances, set the same
`DOCKERINO_PIPE_NAME` in both processes.

```powershell
dotnet @cli launch start <project-id> <action-id>
dotnet @cli launch list
dotnet @cli launch inspect <launch-id>
dotnet @cli launch stop <launch-id>
dotnet @cli session list
dotnet @cli session inspect <session-id>
dotnet @cli session output <session-id>
dotnet @cli session follow <session-id>
dotnet @cli session stop <session-id>
dotnet @cli session reset <session-id>
```

In a terminal created by a configured launch, `dockerino reset` replaces that
terminal and replays the original process's complete command sequence in its
original working directory. Other terminals are unaffected. The replacement
has a new session ID and does not retain the previous shell state or output;
commands and their side effects run again. Detached Docker containers are not
stopped. The explicit session-ID form works outside the terminal. Both forms
return acceptance rather than the final outcome; use `launch inspect` to see
pending, succeeded, or failed reset outcomes. A self-reset can terminate its own
CLI process before the response is displayed. Reset requires a running app and
does not start it. See
[RUN-019](specs/launches-and-sessions.md#reset-request-and-outcome-contract-open).

The terminal forwards `dockerino` commands to the CLI executable. Place
`DockerinoLamborgino.Cli.exe` and its accompanying build or publish files beside
the application, add the CLI to `PATH`, or set `DOCKERINO_CLI_PATH` to its
absolute executable path before starting the application. For example, after a
Debug build from source, the executable is under
`src\DockerinoLamborgino.Cli\bin\Debug\net10.0`. The shorthand `reset` requires
the session context supplied to configured terminals; it is not supported in an
independent **New terminal**. The explicit form does not require that context.

Launches and sessions, including terminal output, are held in memory for the
application lifetime. Exiting the application stops its managed sessions.
Stopping a terminal does not stop detached Docker containers.

## Git repository operations

Add repository roots to a project explicitly; the application does not
discover repositories. The dashboard shows each associated repository's
working-tree status and local/remote branches. Switching affects one
repository at a time; selecting a remote branch creates a local tracking branch
when needed. Fetch and fast-forward-only pull are explicit operations.

The CLI exposes only the Git operations where Dockerino adds application-specific
behavior: batch fetch and fast-forward-only pull. Use the application UI for
repository status and branch switching, or use Git directly for those operations
in a terminal. The CLI accepts repository paths associated with the selected
project:

```powershell
dotnet @cli git fetch $projectId "C:\My Projects\Example" "C:\Other\Repository"
dotnet @cli git pull $projectId "C:\My Projects\Example" "C:\Other\Repository"
```

Each path must be associated with the project. Batch operations continue after
an individual repository fails and report each result. Fetch uses the current
branch's upstream remote or, if none exists, the sole configured remote. Pull
requires an upstream and is fast-forward-only. Neither operation runs
automatically. Git terminal prompts are disabled; configure noninteractive
credentials or authenticate separately. Git hooks and user configured
credential helpers are not sandboxed.

## CLI skill for AI agents

The [`dockerino-lamborgino-cli` skill](https://github.com/AlvaroGReg/alvarogreg-skills/tree/main/skills/dockerino-lamborgino-cli)
helps AI agents use the CLI to configure projects, operate on associated Git
repositories, run saved actions, and inspect or stop managed sessions. Install
it with the Skills CLI:

```powershell
npx skills add AlvaroGReg/alvarogreg-skills --skill dockerino-lamborgino-cli
```

## Status and limitations

The project-registration, configuration, Git, and configurable-launch
workflows are implemented. The UI uses a dark, muted-turquoise theme; the
dashboard shows project tools after project selection and terminal tabs when
sessions exist. Terminal tabs, project rows, and the floating launcher use amber
for starting sessions and red for failures; project and launcher summaries clear
when their relevant sessions no longer need attention.

The terminal integration uses EasyWindowsTerminalControl 1.0.38 (declared MIT)
and depends directly on CI.Microsoft.Terminal.Wpf and
Microsoft.Windows.Console.ConPTY. Its hosted HWND has WPF airspace limitations,
and the upstream control relies on beta APIs. Review the full dependency and
license graph before distribution.

See the [product specifications](specs/README.md) for normative requirements
and acceptance scenarios, [the development plan](plan.md) for implementation
status, and [decision context](decisions.md) for technical rationale and
alternatives.
