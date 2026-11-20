# dockerino-lamborgino

Windows desktop application targeting .NET 10. Phase 3 provides project
registration and manual configuration through the UI and CLI. Git operations
and execution of saved launch configurations are not yet implemented.

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

Actions are displayed by name, color value, and description, but cannot be
run until Phase 5. Branch operations are not yet available. For isolated
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
`actions` array. Each action has a unique GUID `id`, `name`, `color` in
`#RRGGBB` format, optional `description`, and a nonempty `processes` array.
Each process has a unique GUID `id`, `name`, existing absolute
`workingDirectory`, and a nonempty `steps` array. Each step has a unique GUID
`id`, a `command`, and optional `dependsOnSuccessOf`, which must refer to an
earlier step in that process. Array order records the intended order; execution
and dependency handling are not implemented yet. Generate IDs explicitly
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
observations, and evidence limitations.
