# Test suites

The tests are grouped by execution model and feature area:

```text
tests/
  DockerinoLamborgino.Core.Tests/
    Configuration/  Project registration, validation, persistence, and revisions
    Git/            Repository service and Git CLI behavior
    Launch/         Launch orchestration and CLI syntax
    Sessions/       In-memory session ownership and lifecycle
    Shell/          Shell visibility state
  DockerinoLamborgino.Terminal.Tests/
                  Windows/ConPTY process-isolation integration test
  DockerinoLamborgino.App.SequenceChecks/
                  Standalone command-sequence protocol checks
  UiSmoke/
                  Interactive Windows UI Automation smoke tests
```

## Automated test projects

Run all xUnit tests included in the solution:

```powershell
dotnet test DockerinoLamborgino.sln
```

The Core test project does not target Windows; its Git tests require Git on
`PATH`. The Terminal tests require Windows, the .NET 10 SDK, Windows Desktop
runtime, and ConPTY support. To run that test with locked package restore:

```powershell
dotnet test tests/DockerinoLamborgino.Terminal.Tests/DockerinoLamborgino.Terminal.Tests.csproj -p:RestoreLockedMode=true
```

## Standalone command-sequence checks

These are executable protocol checks rather than xUnit tests, so
`dotnet test` does not run them. They start PowerShell directly, not through
ConPTY:

```powershell
dotnet run --project tests/DockerinoLamborgino.App.SequenceChecks/DockerinoLamborgino.App.SequenceChecks.csproj
```

## Interactive UI smoke tests

Build the Debug app first. These UI Automation checks require an interactive
Windows desktop and Windows PowerShell in STA mode:

```powershell
powershell.exe -NoProfile -STA -File tests/UiSmoke/WindowAndTerminalSessionsUiSmoke.ps1
powershell.exe -NoProfile -STA -File tests/UiSmoke/ProjectConfigurationPersistenceUiSmoke.ps1
powershell.exe -NoProfile -STA -File tests/UiSmoke/GitRepositoryManagementUiSmoke.ps1
```

`WindowAndTerminalSessionsUiSmoke.ps1` checks window controls and independent
terminal-session state. `ProjectConfigurationPersistenceUiSmoke.ps1` exercises
project creation, review and save, editing across an app restart, stale-revision
conflict handling, corrupt-file isolation, and verifies that saving does not run
configured commands. `GitRepositoryManagementUiSmoke.ps1` covers Git repository
management through the UI and also requires Git on `PATH`.
