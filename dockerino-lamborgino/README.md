# dockerino-lamborgino

Windows desktop application targeting .NET 10. Project registration, manual
configuration, and explicitly requested Git operations are available through
the UI and CLI. Phase 5 launch execution is under development and has not yet
been accepted; see the limitations below.
The dashboard uses a dark, muted-turquoise visual treatment; configuration
dialogs use WPF's system theme. The Phase 6 design and interaction still need
user review and manual verification.

## Build and run

Install the .NET 10 SDK, Windows Desktop runtime, and PowerShell 7 (`pwsh.exe`)
on Windows. From this directory:

```powershell
dotnet build DockerinoLamborgino.sln -c Debug
dotnet build DockerinoLamborgino.sln -c Release
dotnet test DockerinoLamborgino.sln
dotnet run --project .\src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj
```

## Register and configure a project in the UI

Choose **Find project** and select one directory, or use **Manage projects…**
to edit a saved project. Enter the project name and directory and edit the
launch configuration as a JSON **array of actions**, using the `actions`
portion of the example below. `[]` saves a project without actions. Select
**Review and save…** to inspect the values before confirming. Select a saved
project and **Edit selected** to change it later. Validation and storage
errors appear in the window; a conflicting edit must be reloaded.

Enter associated repository roots explicitly as a JSON array of absolute paths;
these may be outside the project directory. Existing projects default to an
empty array. Actions are displayed by name, color value, and description. For isolated
testing, the application accepts `--project-storage <absolute-directory>`;
this overrides its default project store.

## Project configuration CLI

The CLI and application use the shared project configuration service. By default
projects are stored individually under `%LOCALAPPDATA%\DockerinoLamborgino\projects`.
An absent store contains no projects. Registration selects only the directory
given; it does not scan for projects, infer commands, or run commands.

From this directory, register a project and create an editable file containing
only its project object. Replace the path with an existing absolute directory:

```powershell
$result = dotnet run --project .\src\DockerinoLamborgino.Cli -- register "Example" "C:\My Projects\Example" | ConvertFrom-Json
$projectId = $result.project.id
$revision = $result.revision
$projectFile = ".\project-edit.json"
$result.project | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $projectFile
dotnet run --project .\src\DockerinoLamborgino.Cli -- list
dotnet run --project .\src\DockerinoLamborgino.Cli -- get $projectId
# Edit project-edit.json, then validate and save.
dotnet run --project .\src\DockerinoLamborgino.Cli -- validate $projectFile
dotnet run --project .\src\DockerinoLamborgino.Cli -- save $projectFile $revision
```

`register` creates an empty action list. `get` and `list` return JSON with a
`source`, `project`, validation `issues`, and a `revision`; unreadable or
malformed files may have no project or revision. Edit
the `project` object in a separate JSON file, then validate and save it with
the revision from `get`. A stale revision is rejected; reload before editing
again. `save` without a revision is create-only. Errors are reported rather
than inventing missing values. A damaged project file is reported separately
from other projects. `list` can succeed while reporting issues for individual
entries, so inspect each entry's `issues`. Exit codes are 0 for success, 1 for
an operation error, and 2 for invalid CLI syntax.

Each project has a nonempty unique GUID `id`, `name`, existing absolute `directory`, and an
optional `repositories` array of distinct existing absolute directory paths, and an
`actions` array. Each action has a unique GUID `id`, `name`, `color` in
`#RRGGBB` format, optional `description`, and a nonempty `processes` array.
Each process has a unique GUID `id`, `name`, existing absolute
`workingDirectory`, and a nonempty `steps` array. Each step has a unique GUID
`id`, a `command`, and optional `dependsOnSuccessOf`, which must refer to an
earlier step in that process. Steps run in array order and a failure skips all
later steps, whether or not they declare a dependency. Existing dependencies
remain valid and need no migration. Generate IDs explicitly
(for example, with `[guid]::NewGuid()` in PowerShell); IDs must be unique
within the project. The configuration may contain no actions.
Names and commands must not be blank. A description may be omitted but cannot
be null. Unknown JSON properties are rejected; property names are
case-insensitive.

For example, the project object in the file passed to `validate` and `save`
can have this shape (replace IDs and paths with your own):

```json
{
  "id": "c9bd5995-661e-4b7e-b243-e3e5cd8dc250",
  "name": "Example",
  "directory": "C:\\My Projects\\Example",
  "repositories": ["C:\\My Projects\\Example", "C:\\Other\\Repository"],
  "actions": [{
    "id": "3c7cf820-40c6-49fa-8ef9-27037c9e243d",
    "name": "Development",
    "color": "#3377AA",
    "description": "Starts the development process",
    "processes": [{
      "id": "72ad3bc5-f017-4784-813c-f8735d5570dd",
      "name": "Main",
      "workingDirectory": "C:\\My Projects\\Example",
      "steps": [{
        "id": "3c1bc722-f448-4230-910e-682ab4681432",
        "command": "example-command",
        "dependsOnSuccessOf": null
      }]
    }]
  }]
}
```

## Launches: initial Phase 5 implementation

Select a project and one of its saved actions in the main window, then choose
**Run selected action**. Each process uses a separate PowerShell 7 terminal.
The CLI communicates with the running application via a current-user named
pipe. If needed, it starts the application with the dashboard hidden.
Set `DOCKERINO_APP_PATH` to the absolute path of the application executable if
it is not beside the CLI executable. For isolated instances, set the same
`DOCKERINO_PIPE_NAME` in both processes.

```powershell
dockerino-cli launch start <project-id> <action-id>
dockerino-cli launch list
dockerino-cli launch inspect <launch-id>
dockerino-cli launch stop <launch-id>
dockerino-cli session list
dockerino-cli session inspect <session-id>
dockerino-cli session output <session-id>
dockerino-cli session follow <session-id>
dockerino-cli session stop <session-id>
dockerino-cli dashboard open
```

Use the actual CLI executable path in place of `dockerino-cli`. Launches and
sessions are kept in memory for the application lifetime. Output includes raw
terminal control sequences and is retained in memory. Stopping a session does
not stop detached Docker containers.

**Limitations:** Phase 5 remains in progress. The UI does not yet present
complete per-step progress or launch controls, and environment validation does
not identify all required tools before execution. PowerShell compound commands
may not produce an unambiguous step exit status. Physical keyboard interaction,
exit confirmation, live following and the complete UI/CLI workflow still need
independent verification. Phase 6 launcher and dashboard behavior also require
manual verification; see [Phase 6 evidence](PHASE-6-EVIDENCE.md).

## Repository operations

Use **Manage Git…** to select a saved project, inspect its associated
repositories and their working-tree status, and switch a single local or
remote branch. Select one or more repositories for explicit fetch or
fast-forward-only pull. Results are reported individually. Repository paths
must identify working-tree roots, not directories inside another repository.
Projects without valid Git repositories remain available for configuration.

The CLI accepts the exact associated paths returned by `get`:

```powershell
dotnet run --project .\src\DockerinoLamborgino.Cli -- git status $projectId
dotnet run --project .\src\DockerinoLamborgino.Cli -- git switch $projectId "C:\My Projects\Example" local main
dotnet run --project .\src\DockerinoLamborgino.Cli -- git switch $projectId "C:\My Projects\Example" remote origin/feature
dotnet run --project .\src\DockerinoLamborgino.Cli -- git fetch $projectId "C:\My Projects\Example" "C:\Other\Repository"
dotnet run --project .\src\DockerinoLamborgino.Cli -- git pull $projectId "C:\My Projects\Example"
```

Fetch uses the current branch's upstream remote or, if there is no upstream,
the sole configured remote. Pull requires an upstream and refuses divergence;
its fetch may nevertheless update remote-tracking references. Neither operation
runs automatically. No stash, reset, commit, push, or merge is requested.
Git uses the user's configured credentials; terminal prompts are disabled and
interactive authentication is not supported by this integration. Configure
noninteractive credentials or authenticate separately before retrying. Git
itself and user-configured helpers or hooks are not sandboxed.

New terminal sessions start PowerShell; the session list is in memory only.
Hiding the menu or collapsing the window does not intentionally stop a session.
Stop terminates only the selected session's owned Job Object. Exiting the app
stops all owned sessions. A detached Docker container is managed by the Docker
daemon and is not stopped by closing its client or the application.

The terminal integration uses EasyWindowsTerminalControl 1.0.38 (MIT as
declared by the package); its direct dependencies include
CI.Microsoft.Terminal.Wpf 1.25.260303002 and
Microsoft.Windows.Console.ConPTY 1.24.260710001. The hosted HWND has WPF
airspace limitations and the upstream control relies on beta APIs. Review the
full dependency and license graph before distribution.

See [PHASE-2-EVIDENCE.md](PHASE-2-EVIDENCE.md) for Phase 2 results, user
observations, and evidence limitations, and [PHASE-4-EVIDENCE.md](PHASE-4-EVIDENCE.md)
for Git verification and outstanding acceptance.
