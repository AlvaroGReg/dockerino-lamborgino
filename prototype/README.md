# ConPTY integration prototype

This spike validates the integration of WPF/.NET 10, EasyWindowsTerminalControl, and Job Objects. It is not yet part of the launcher and must not be run from one of the site workspace repositories.

## Run

From this directory:

```powershell
dotnet run --project .\TerminalSpike\TerminalSpike.csproj
```

The application opens `pwsh.exe -NoLogo -NoProfile -NoExit` inside EasyWindowsTerminalControl and assigns the root process to a session-exclusive Job Object. The status bar shows the actual PID and updates whether the shell is active or has exited, including its exit code.

To test a real command, open one instance per session and pass its directory:

```powershell
dotnet run --project .\TerminalSpike\TerminalSpike.csproj -- --command 'pwsh.exe -NoLogo -NoExit -Command "dotnet watch run --no-hot-reload"' --working-directory 'C:\ruta\al\backend' --title 'Backend'
dotnet run --project .\TerminalSpike\TerminalSpike.csproj -- --command 'pwsh.exe -NoLogo -NoExit -Command "npm start"' --working-directory 'C:\ruta\al\app' --title 'Frontend'
dotnet run --project .\TerminalSpike\TerminalSpike.csproj -- --command 'pwsh.exe -NoLogo -NoExit -Command "npm start"' --working-directory 'C:\ruta\al\backOffice' --title 'BackOffice'
```

`--log-file <path>` saves ConPTY output for diagnostics. `--title <text>` distinguishes windows during a multi-session spike.

### Passive startup reproduction

```powershell
.\TerminalSpike\Test-TerminalStartup.ps1
```

The script builds Debug, opens a window titled `TerminalSpike-Fase1-startup`, and checks the actual PID, connection, handoff, prompt in ConPTY output, and process survival for three seconds. It does not inject keystrokes. It leaves the window open for physical keyboard acceptance and provides a command that creates a unique marker file, independent of command echo. The unique log remains in `%TEMP%` and contains ConPTY output and process events. Logged output alone does not prove rendering. To isolate cmd: `-Command 'cmd.exe /K' -PromptPattern '[A-Za-z]:\\[^\r\n]*>' -WindowTitle 'TerminalSpike-Fase1-cmd'`. Use `-NoBuild` if already built and another window is open: Windows locks the running executable against replacement.

The window may come to the foreground when opened, like any normal WPF application. The script does not attempt to change the focus, size, or position of other windows.

## Design verified by compilation

- The control's internal `TermPTY` is disconnected to prevent it from starting its own shell. After `TermReady`, the session's `TermPTY` is connected to `EasyTerminalControl`; output read before that handoff is buffered and replayed.
- `JobObjectProcessFactory` uses the package's public `IProcessFactory` interface and internally exposes PID/exit code for diagnostics.
- It creates the process with `CREATE_SUSPENDED`, assigns it to the Job Object, and only then calls `ResumeThread`. It does not accept or expose PIDs in the UI.
- The Job Object is configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`; the stop button terminates only that Job Object.
- The ConPTY attribute is retained in the process creation attribute list required by Windows. The upstream factory calls `CreateProcess` with `bInheritHandles: false`; the local factory should be compared with that behavior when diagnosing startup.
- Pinned version: `EasyWindowsTerminalControl 1.0.38`, with `net10.0-windows` available in the package.

## Phase 1 results

- **Build:** Debug and Release build on Windows with .NET SDK 10.0.404, without errors or warnings. The earlier NuGet analysis detected no vulnerable packages in the spike. Dependencies were not changed.
- **ConPTY/control:** package 1.0.38 implements `TermPTY` as the ConPTY backend and connects `TerminalControl` when `EasyTerminalControl.ConPTYTerm` is set; the latter is hosted as an HWND. The upstream README confirms the output/input flow and the airspace limitation. Output received by `WriteToTerm` proves the API→ConPTY→log path, not physical input capture by the HWND. The control's `AutoResize` connects terminal size changes to `TermPTY.Resize`; subsequent visual acceptance is documented below.
- **Job Object:** the factory creates the shell suspended, assigns it to the Job Object, and then resumes it. The test started a descendant PowerShell process; stopping the session terminated both processes and left an unrelated PowerShell process alive. Closing the app also terminated the session.
- **Paths:** a temporary path containing spaces and the actual `EmbutidosEzequiel\Ezequiel` path were checked, finding `Ezequiel.csproj` without inferring the directory name from the repository.
- **Real commands:** in separate ArvaHoteles sessions, `dotnet watch run --no-hot-reload` started the backend and served a local request; frontend `npm start` compiled successfully with webpack in 36.4 seconds; backOffice `npm start` ran `prestart`/`combine`, compiled Angular, and listened on `localhost:4200`. All three sessions were closed and the ports stopped listening.
- **Docker:** an Alpine container was started with `up -d` in an isolated Compose project. Closing the terminal session left the container running; `docker compose stop` stopped it, and `docker compose down` removed only the test container and network. No volumes were used and unrelated containers were untouched. Docker Desktop remains running and the `alpine:3.21` image remains cached.
- **Test cleanup:** the 93 pre-existing `wwwroot/dist` files, versioned backOffice JSON files, and Angular cache were restored. The ArvaHoteles repository was clean again.

### Manual acceptance completed

The initial physical test was blocked: “Sesión asignada a un Job Object” was displayed even though that status did not prove the process was alive. The initial search for `pwsh.exe` did not establish the state of the previous configuration, which used `powershell.exe`. The initial log had no PID/exit code and did not observe errors after `TermReady`.

**Isolated cause and fix:** under the launcher with `UseShellExecute=false`, the prompt appeared in the parent console output rather than ConPTY; `pwsh` exited with code 0. Changing only `STARTUPINFO` to use `STARTF_USESTDHANDLES` with all three handles null caused Windows to supply the pseudoconsole handles: in the same scenario, the prompt appears in ConPTY and `pwsh` remains alive. Without the change the regression failed; with it, the test passes. This identifies coupling to host standard handles in the reproduced scenario, not a cause for every possible black screen. Changing only `bInheritHandles` did not fix it. Suspended assignment to the Job and version 1.0.38 remain unchanged.

**Evidence, 2026-11-19:** Debug and Release build without warnings; `pwsh.exe -NoLogo -NoProfile -NoExit` and `cmd.exe /K` passed the passive prompt and three-second survival test. `cmd.exe /c exit 23` logged the PID and `PROCESS_EXITED ... exitCode=23`. An intermediate build failed because our own experimental window held the executable locked; after closing it, both builds passed. Duplicate logging was removed and replay of the buffer before connection was serialized. State distinguishes a live process from an exited process and retains the exit code after handles are released; startup/reader errors are logged. `TermReady` still indicates only that the backend and writer are ready, not acceptance.

The automated passive test **does not replace** human acceptance criteria. On 2026-11-19 the user confirmed rendering, physical keyboard input, and resizing with line adjustment, accompanied by screenshots of `TerminalSpike-Fase1-ACEPTACION` and active PID 24436. The user typed and ran `Set-Content` in the terminal; `%TEMP%\TerminalSpike-physical-24436.txt` was verified to contain exactly `TERMINALSPIKE_PHYSICAL_KEYBOARD_MARKER`. The second screenshot shows the command and prompt after narrowing the window. The earlier simulated-event test is not used as physical acceptance. With this evidence and the earlier tests, the Phase 1 blocker is considered resolved; the full UI has not been started.

### Observed warnings not caused by the spike

- The backend shows NU1902/NU1903 warnings for `System.Security.Cryptography.Pkcs 10.0.12`; site dependencies were not updated.
- Starting all three processes at once produced transient `dotnet watch` warnings (“Too many changes at once”) while the frontends generated files. The backend server did start and respond.
- Angular reported duplicate Material styles, but completed compilation.

## Dependencies and license

- `EasyWindowsTerminalControl 1.0.38` declares MIT.
- Direct dependencies pinned by that package: `CI.Microsoft.Terminal.Wpf 1.25.260303002` and `Microsoft.Windows.Console.ConPTY 1.24.260710001`.
- The upstream README warns that the WPF backend depends on beta packages/APIs and documents the hosted HWND's airspace limitation.
- Before distributing the launcher, review notices/licenses throughout the NuGet dependency graph and preserve applicable notices.
