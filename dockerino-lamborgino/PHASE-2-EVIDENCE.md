# Phase 2 evidence — 2026-11-19

**Gate: accepted by the user.** Automated checks passed and the user performed
manual tests of the application. The observations and limits below distinguish
what was reported from checks that were not individually confirmed.

## Requirements and evidence

| Objective | Evidence | Remaining |
| --- | --- | --- |
| P2-01 workspace/build | `DockerinoLamborgino.sln`, .NET 10 WPF app, non-WPF core, and two test projects under `dockerino-lamborgino/` in this repository. Debug and Release builds and tests are recorded below. | None identified for this objective. |
| P2-02 project/session boundaries | Core tests cover identity, selection, transitions, unknown IDs, failed starts/stops, ownership, and visibility state. Session state is in memory only. | The future CLI needs its own integration. |
| P2-03 floating shell | `WindowStyle=None`, `Topmost=True`, collapse/expand, and a hideable menu; unavailable areas are labeled. UI Automation exercised controls. | The user did not specifically report a visual always-on-top or positioning check. |
| P2-04 terminal/process ownership | Adapter uses EasyWindowsTerminalControl and one Job Object per session. An integration test starts two ConPTY processes and verifies stopping one leaves the other alive. The user reports creating consoles and running basic and long-running processes, npm and Docker. | The user reports that Tab removes terminal focus instead of completing input. Resize and descendant-process cleanup were not specifically reported. |
| P2-05 navigation/lifecycle | UI Automation selected two sessions, hid the menu, collapsed and expanded the window, checked both reported running, stopped one and exited. The user reports stopping consoles and closing the application. | OS-level process-tree cleanup and interaction after reselection or collapse were not separately confirmed in the user's report. |
| P2-06 gate | The user requested that Phase 2 tests be marked complete; this report records the results, known issue and evidence limits. | Terminal keyboard handling is a future improvement. |

## Checks

Initially executed on 2026-11-19 in the former external workspace on Windows
with SDK 10.0.404, exit code 0 unless noted. These historical commands use the
project names in effect at the time, before the rename:

```powershell
dotnet clean Dockerino.sln -c Debug
dotnet clean Dockerino.sln -c Release -v quiet
dotnet build Dockerino.sln -c Debug
dotnet build Dockerino.sln -c Release
dotnet test Dockerino.sln -c Debug --no-build
dotnet test Dockerino.sln -c Release --no-build
pwsh -NoProfile -File .\Smoke-Window.ps1
dotnet list src/Dockerino.App/Dockerino.App.csproj package --vulnerable --include-transitive
```

Both builds had zero warnings and errors; each test configuration passed 9 core
tests and 1 real ConPTY isolation test. The smoke script passed its application
state and UI Automation checks. NuGet reported no vulnerable app packages in the
configured sources. The first extended smoke run failed because UI Automation
exposed the session item's type name instead of its label; the accessible name
binding was corrected and the test passed afterward.

After moving the application into this repository, the following were run
from the then-named `dockerino/` directory on 2026-11-20, all with exit code 0:

```powershell
dotnet build Dockerino.sln -c Debug
dotnet build Dockerino.sln -c Release
dotnet test Dockerino.sln -c Debug --no-build
dotnet test Dockerino.sln -c Release --no-build
pwsh -NoProfile -File .\Smoke-Window.ps1
```

Both builds had zero warnings and errors. Each test configuration passed 9 core
tests and 1 ConPTY isolation test; the UI Automation smoke test also passed.

After renaming the project to dockerino-lamborgino, the following were run from
`dockerino-lamborgino/`, all with exit code 0:

```powershell
dotnet build DockerinoLamborgino.sln -c Debug
dotnet build DockerinoLamborgino.sln -c Release
dotnet test DockerinoLamborgino.sln -c Debug --no-build
dotnet test DockerinoLamborgino.sln -c Release --no-build
dotnet test tests/DockerinoLamborgino.Terminal.Tests/DockerinoLamborgino.Terminal.Tests.csproj -p:RestoreLockedMode=true
pwsh -NoProfile -File .\Smoke-Window.ps1
```

Both builds had zero warnings and errors; all 10 tests passed in each
configuration, the locked-restore terminal test passed, and the UI Automation
smoke test passed.

The integration test checks root processes, not descendants, and observes the
unaffected process for 500 ms. It does not prove terminal rendering or input.
The smoke script checks application-reported state, not terminal content.

## User-reported manual checks

On 2026-11-20, the user reported creating consoles, running basic and
long-running processes, stopping consoles, and closing the application. The user
reported npm and Docker working, and that Docker did not stop when the console
was stopped. The user considered the tests satisfactory overall and requested
that the Phase 2 tests be marked complete.

The user also reported that Tab does not autocomplete and instead moves focus
away from the console. This is a known, unresolved issue; terminal keyboard
handling and shortcuts are recorded in the development plan as an optional
future improvement. The report did not specify separate checks for resize,
always-on-top behavior, interaction after collapse or reselection, or OS-level
verification of descendant-process cleanup.

## Risks and dependencies

EasyWindowsTerminalControl 1.0.38 declares MIT. The resolved terminal graph also
contains CI.Microsoft.Terminal.Wpf 1.25.260303002 and
Microsoft.Windows.Console.ConPTY 1.24.260710001. A full license/notice review
is still required before distribution. The upstream hosted HWND has WPF airspace
limitations and uses beta APIs. Restrictive parent-job policies may prevent job
assignment; a process allowed to break away is not covered by the job. Closing
the shell or application does not stop detached Docker containers.
