# Terminal session integration test

Run on Windows with the .NET 10 SDK, Windows Desktop runtime, and ConPTY support:

```powershell
dotnet test tests/DockerinoLamborgino.Terminal.Tests/DockerinoLamborgino.Terminal.Tests.csproj -p:RestoreLockedMode=true
```

The test follows the production terminal library's `TermPTY`/factory startup pattern,
without creating a window or terminal control. It starts two `cmd.exe` processes,
waits up to 15 seconds for each terminal to become ready, and checks distinct
PIDs and live processes. Closing the first factory must end its process while
the second remains alive and its terminal run task remains active; closing the
second must end its process. Exit waits are limited to 15 seconds. Both jobs are
disposed even on failure, so cleanup targets only processes created by the test.

This checks process isolation, not GUI rendering, keyboard input, terminal output
correctness, descendant processes, or the application's `TerminalSessionView`.
The test references the production terminal library. A live process is observed
for 500 ms after the first exit, not indefinitely.
