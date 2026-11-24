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
  DockerinoLamborgino.LocalCommandServer.Tests/
                  Named-pipe response-delivery checks
  DockerinoLamborgino.App.SequenceChecks/
                  Standalone command-sequence protocol checks
  GitPanelSelectionChecks/
                  Standalone delayed Git panel selection checks
  DrainingTerminalIdleBenchmark/
                  Noninteractive idle ConPTY host CPU measurement
  SessionOutputBenchmark/
                  In-process incremental output microbenchmark
  UiSmoke/
                  Interactive Windows UI Automation smoke tests
  TerminalTabIntegration/
                  Interactive real-terminal keyboard integration check
```

`UiSmoke/IsolatedApplicationShutdownSmoke.ps1` starts an isolated application
and test-owned process tree, moves the mouse cursor, and invokes the launcher's
exit confirmation. It requires an interactive desktop and should only be run
when those interactions will not disrupt the user.

## Automated test projects

### Repeatable Windows verification (`REL-030`, `REL-034`)

From the repository root on Windows, with PowerShell 7, Git, the .NET 10 SDK,
Windows Desktop runtime, and ConPTY available:

```powershell
pwsh -NoProfile -File scripts/verify-windows.ps1
```

The script restores with locked mode for projects with lock files, builds Debug
and Release, runs the solution's xUnit tests and the separate local command
server xUnit project in Release with TRX and Cobertura collection, then runs
the standalone sequence and Git panel selection checks.
It exits nonzero on a
failed command or missing test/coverage reports. Each run creates a unique
directory under `artifacts/verification/` with a transcript containing the
revision, working-tree status, OS/architecture, tool versions, commands and
outcomes, plus test and coverage files. A dirty working tree means the revision
alone does not identify the tested source; retain the changes separately.
Override the output location with `-ResultsRoot <directory>` if needed.

The Windows GitHub Actions workflow runs this same entry point on pushes, pull
requests, and manual dispatches. It uploads available evidence even on failure
and retains it for 30 days; an artifact's presence does not mean the run passed.
CI evidence is tied to the checked-out revision in the transcript. An actual CI
run is needed to establish its outcome. Neither local nor CI execution includes
the interactive desktop or clean-machine packaging gates below.

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

The local command server test project is not in the solution. Run it separately:

```powershell
dotnet test tests/DockerinoLamborgino.LocalCommandServer.Tests/DockerinoLamborgino.LocalCommandServer.Tests.csproj -c Release
```

## Coverage and the 1.1.0 baseline

After building Release, collect the xUnit coverage report with:

```powershell
dotnet test DockerinoLamborgino.sln --no-build --no-restore -c Release `
  --collect "Code Coverage;Format=Cobertura" `
  --results-directory artifacts/audit-coverage
```

The review's measured results and revision are recorded in the
[development plan](../plan.md#review-verification-baseline), not maintained as
a second coverage table here. That run measured Core, CLI, and Terminal;
App/WPF was not included. Test assemblies also appear in the collector's
output: exclude them when calculating product metrics. Do not interpret the
report's aggregate as whole-product coverage, or treat the standalone and
interactive checks below as part of that measurement.
The repeatable entry point also collects the separate server project's coverage.
That project compiles `LocalCommandServer.cs` into its test assembly, so its
coverage is not App/WPF assembly coverage and needs separate interpretation.

### Requirement mapping and planned regressions

These pointers and scenario mappings identify checks, not claims that each
acceptance case is covered or has passed.

| Existing suite / execution model | Related requirements | 1.1.0 gaps to close |
|---|---|---|
| `DockerinoLamborgino.Core.Tests/Configuration/` — xUnit | `CFG-*`, `UI-011`, `UI-012` | `CFG-025`: save a valid project, make an unrelated resource unavailable, and verify operation-scoped eligibility and diagnostics through the app integration. |
| `DockerinoLamborgino.Core.Tests/Git/` — xUnit; `GitPanelSelectionChecks/` — standalone; Git UI smoke — interactive | `GIT-*` | `GIT-034`: deterministic panel checks cover delayed selection changes; interactive and broader integration evidence remains separate. |
| `DockerinoLamborgino.Core.Tests/Launch/` and `Sessions/` — xUnit; reset integration and terminal smoke — interactive desktop | `RUN-001`–`RUN-007`, `RUN-012`–`RUN-015`, `RUN-019`–`RUN-025`, `RUN-031`, `RUN-032` | `RUN-014`/`RUN-022`: terminal smoke covers output/follow after closing both session types; `RUN-019`: integrated stop/tab-close races remain open. |
| `DockerinoLamborgino.Terminal.Tests/` — real ConPTY without a window | `RUN-013`, `RUN-030`, `REL-033` | Tests cover isolated roots and descendant trees; creation failures and application shutdown need integrated verification. |
| `DockerinoLamborgino.App.SequenceChecks/` — standalone executable | `RUN-003`, `RUN-004`, `RUN-019` | Direct PowerShell protocol checks do not establish real ConPTY/UI lifecycle behavior. |
| `DockerinoLamborgino.LocalCommandServer.Tests/` — xUnit, linked server source | `RUN-019` | Disconnected response delivery is exercised at the pipe handler; full application reset and integrated lifecycle races remain separate. |
| `UiSmoke/` and `TerminalTabIntegration/` — interactive desktop | `UI-*`, `RUN-010`, `RUN-017`, `RUN-018`, `REL-033` | Run and record remaining visual, interaction, multi-monitor, sign-in, and lifecycle cases. |
| No dedicated output benchmark or installer acceptance suite yet | `REL-022`, `REL-025`, `REL-027`, `REL-035` | Record output workloads/budgets and comparable measurements; verify installation/upgrades/uninstall, dependencies, PATH, and persistence on clean Windows x64. |

#### Scenario-level examples

Methods are in the named test class under the suite paths above. Each mapping
is limited to the stated scenario, not every aspect of the requirement.

| Requirement | Scenario and check |
|---|---|
| `CFG-002`, `CFG-021` | Validation rejects invalid dependencies without executing configured commands: `ProjectConfigurationServiceTests.ValidationChecksDependenciesAndDoesNotRunCommands`. |
| `CFG-022` | A stale update is rejected: `ProjectConfigurationServiceTests.RegisterEditAndRejectStaleRevision`; simultaneous updates have one winner: `SimultaneousUpdatesAllowOnlyOneWriter`. |
| `CFG-023` | A damaged project does not hide another: `ProjectConfigurationServiceTests.DamagedProjectDoesNotHideOthers`. |
| `GIT-012` | A conflicting branch switch preserves local changes: `GitRepositoryServiceTests.SwitchPreservesConflictingChanges`. |
| `GIT-021`, `GIT-023` | Remote selection and batch failures: `GitRepositoryServiceTests.FetchRulesTrackingAndBatchFailures`. |
| `GIT-022` | Fast-forward pull succeeds and divergence is rejected: `GitRepositoryServiceTests.PullFastForwardsAndRejectsDivergence`. |
| `GIT-032` | CLI fetch/pull batches use associated paths only: `GitCliTests.FetchAndPullBatchUseOnlyAssociatedPaths`. |
| `GIT-034` | Delayed successes and errors after switching, clearing, or returning to a selection: `GitPanelSelectionChecks` (`SwitchSelection`, `ClearSelection`, `ReturnToSameProject`, `CurrentErrorAndSelectionReset`). |
| `RUN-002`, `RUN-003`, `RUN-004` | Independent processes and failure skipping: `LaunchOrchestratorTests.ProcessesRunIndependentlyAndFailureSkipsLaterSteps`; direct PowerShell ordered/success/failure protocol cases: sequence checks `CheckAsync`. |
| `RUN-012`, `RUN-013` | Unknown IDs cannot affect owned sessions: `SessionRegistryTests.UnknownIdsCannotAffectOwnedSessions`; root and descendant isolation: `ConPtySessionIsolationTests` and `ConPtyDescendantContainmentTests` (not integrated app-shutdown coverage). |
| `RUN-019` | Snapshot replay leaves another process alone: `LaunchOrchestratorTests.ResetReplaysSnapshotAndLeavesOtherProcessAlone`; direct shell context forwarding: sequence checks `CheckInteractiveResetAsync`. |
| `RUN-019` | A disconnected pipe still invokes the post-response callback once: `ResponseDeliveryTests.AfterResponseRunsWhenClientDisconnectsBeforeResponse` (server handler, not full application integration). |
| `RUN-019`, `REL-033` | Real application contextual and explicit reset with an independent session: `UiSmoke/ResetIntegration.ps1` (interactive desktop, separately invoked). |
| `RUN-010`, `REL-033` | Real keyboard Tab/focus behavior: `TerminalTabIntegration` (interactive desktop, separately invoked); this alone does not verify all terminal behaviors. |

The [1.1.0 release ledger](../plan.md#release-acceptance-ledger) owns closure
status and evidence. A local xUnit pass does not close the interactive or
packaging gates. Consult the gaps above before treating any scenario as complete.

## Standalone command-sequence checks

These are executable protocol checks rather than xUnit tests, so
`dotnet test` does not run them. They start PowerShell directly, not through
ConPTY. They also check that a configured shell forwards `dockerino` arguments
and its session context to the CLI executable:

```powershell
dotnet run --project tests/DockerinoLamborgino.App.SequenceChecks/DockerinoLamborgino.App.SequenceChecks.csproj
```

## Standalone Git panel selection checks

The deterministic WPF panel checks use delayed adapter responses and do not open
a window. They are separate from `dotnet test` and the interactive Git UI smoke:

```powershell
dotnet run --project tests/GitPanelSelectionChecks/GitPanelSelectionChecks.csproj -c Release
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
