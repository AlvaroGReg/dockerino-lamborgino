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
  TerminalTabIntegration/
                  Interactive real-terminal keyboard integration check
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
ConPTY. They also check that a configured shell forwards `dockerino` arguments
and its session context to the CLI executable:

```powershell
dotnet run --project tests/DockerinoLamborgino.App.SequenceChecks/DockerinoLamborgino.App.SequenceChecks.csproj
```

## Interactive UI smoke tests

Build the Debug app first. These UI Automation checks require an interactive
Windows desktop and PowerShell in STA mode:

```powershell
powershell.exe -NoProfile -STA -File tests/UiSmoke/WindowAndTerminalSessionsUiSmoke.ps1
pwsh.exe -NoProfile -STA -File tests/UiSmoke/ProjectConfigurationPersistenceUiSmoke.ps1
pwsh.exe -NoProfile -STA -File tests/UiSmoke/ResetIntegration.ps1
powershell.exe -NoProfile -STA -File tests/UiSmoke/GitRepositoryManagementUiSmoke.ps1
```

`WindowAndTerminalSessionsUiSmoke.ps1` checks window controls and independent
terminal-session state. `ProjectConfigurationPersistenceUiSmoke.ps1` exercises
editing a seeded project, review and save, editing across an app restart, stale-revision
conflict handling, corrupt-file isolation, and verifies that saving does not run
configured commands. `GitRepositoryManagementUiSmoke.ps1` covers Git repository
management through the UI and also requires Git on `PATH`.

`ResetIntegration.ps1` builds and starts the real application and CLI with an
isolated project store and pipe. A configured ConPTY shell invokes the real
CLI's contextual reset; a separate CLI process then resets its replacement.
The test checks inspection outcomes, replayed session IDs, rejection of the
replaced ID, and survival of an independent session. It requires PowerShell 7
and an interactive Windows desktop, but does not send mouse or keyboard input.
It does not deterministically hold the reset pending or test a disconnected
client, and does not replace manual visual or interaction acceptance.

## Real terminal Tab integration

Run this separately on an unlocked interactive Windows desktop with the .NET 10
SDK, Windows Desktop runtime, ConPTY, and `pwsh.exe` on `PATH`:

```powershell
dotnet run --project tests/TerminalTabIntegration/TerminalTabIntegration.csproj
```

The check opens a window, moves the mouse and sends real keyboard input. Do not
interact with the desktop until it finishes. It uses PowerShell completion to
verify that Tab reaches the terminal, checks that Tab and Shift+Tab retain
terminal focus, and checks Tab navigation between separate buttons. A timeout or inability to
activate the window is a failure, not a skipped or successful check. It also
checks terminal mouse and keyboard interaction notifications and confirms that
keyboard input in another control is not reported as terminal interaction.
