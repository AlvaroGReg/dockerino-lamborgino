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

On Windows, install:

- .NET 10 SDK to build and run from source.
- .NET 10 Windows Desktop runtime to run the application.
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

## Register and configure a project in the UI

Choose **Find project** and select one directory, or use **Manage projects…**
to edit a saved project. Enter the project name and directory and edit the
launch configuration as a JSON **array of actions**. `[]` saves a project
without actions. Select **Review and save…** to inspect the values before
confirming. Select a saved project and **Edit selected** to change it later.
Validation and storage errors appear in the window; a conflicting edit must be
reloaded.

Enter associated repository roots explicitly as a JSON array of absolute paths;
these may be outside the project directory. Existing projects default to an
empty array. Actions are displayed by name, color value, and description. For
isolated testing, the application accepts
`--project-storage <absolute-directory>` to override its default project store.

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
description, and one or more processes. Each process has a unique GUID, name,
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
```

Launches and sessions, including terminal output, are held in memory for the
application lifetime. Exiting the application stops its managed sessions.
Stopping a terminal does not stop detached Docker containers.

## Git repository operations

Add repository roots to a project explicitly; the application does not
discover repositories. The dashboard shows each associated repository's
working-tree status and local/remote branches. Switching affects one
repository at a time; selecting a remote branch creates a local tracking branch
when needed. Fetch and fast-forward-only pull are explicit operations.

The CLI accepts repository paths associated with the selected project:

```powershell
dotnet @cli git status $projectId
dotnet @cli git switch $projectId "C:\My Projects\Example" local main
dotnet @cli git switch $projectId "C:\My Projects\Example" remote origin/feature
dotnet @cli git fetch $projectId "C:\My Projects\Example"
dotnet @cli git pull $projectId "C:\My Projects\Example"
```

Fetch uses the current branch's upstream remote or, if none exists, the sole
configured remote. Pull requires an upstream and is fast-forward-only. Neither
operation runs automatically. The integration does not request stash, reset,
commit, push, or merge. Git terminal prompts are disabled; configure
noninteractive credentials or authenticate separately. Git hooks and user
configured credential helpers are not sandboxed.

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
sessions exist.

The terminal integration uses EasyWindowsTerminalControl 1.0.38 (declared MIT)
and depends directly on CI.Microsoft.Terminal.Wpf and
Microsoft.Windows.Console.ConPTY. Its hosted HWND has WPF airspace limitations,
and the upstream control relies on beta APIs. Review the full dependency and
license graph before distribution. The application is not currently provided
as a self-contained package; the .NET 10 runtime must be installed.

See [the development plan](plan.md) for implementation status and
[architecture decisions](decisions.md) for technical rationale.
