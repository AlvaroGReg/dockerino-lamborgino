# dockerino-lamborgino — Phase 2 skeleton

Windows desktop application targeting .NET 10. This is an early skeleton, not
the completed project launcher. Project search, saved configurations, Git
operations, and configurable launches are not implemented.

## Build and run

Install the .NET 10 SDK, Windows Desktop runtime, and PowerShell 7 (`pwsh.exe`)
on Windows. From this directory:

```powershell
dotnet build DockerinoLamborgino.sln -c Debug
dotnet build DockerinoLamborgino.sln -c Release
dotnet test DockerinoLamborgino.sln
dotnet run --project .\src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj
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

See [PHASE-2-EVIDENCE.md](PHASE-2-EVIDENCE.md) for the automated results and
the outstanding interactive acceptance checks.
