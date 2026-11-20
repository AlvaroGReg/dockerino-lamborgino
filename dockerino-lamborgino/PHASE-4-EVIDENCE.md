# Phase 4 implementation evidence — 2026-11-20

The application and CLI share Git operations for explicitly associated
repository roots. Saved configurations from earlier phases default to no
repositories. The application does not discover repositories.

## Automated checks

On Windows with .NET SDK 10.0.404 and Git 2.56.1:

- `dotnet build DockerinoLamborgino.sln -c Release --no-restore` passed
  without warnings or errors.
- `dotnet test DockerinoLamborgino.sln -c Debug --no-restore` and the
  equivalent Release command each passed 30 Core/CLI tests and one terminal
  isolation test.
- A Debug solution build using a separate base output directory passed, as
  did Debug and Release WPF builds with separate output directories. The
  standard Debug build could not replace a DLL locked by an already-running
  application; that instance was not closed.
- Git integration tests use disposable local repositories and cover status,
  local and remote switching, conflicting local changes, explicit fetch
  remote selection, missing upstream, fast-forward and divergent pulls,
  tracking conflicts, and partial batch failure. CLI tests use an isolated
  configuration store.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File
  tests/phase4-ui-smoke.ps1` passed with a disposable repository and store.
  It checked project selection and reselection, untracked-file status,
  separate local and remote lists, local branch switching, and refresh while
  preserving project selection.

## Limitations and outstanding acceptance

Real authentication success and failure in the user's Git environment have
not been verified. Interactive authentication is disabled and reported as
unsupported; configured noninteractive credential mechanisms can be used.
The Git tests use local remotes and do not establish real network credential
behavior. Visual review and user acceptance remain outstanding. The automated
tests do not establish complete UI/CLI parity or prove absence of side effects
from user-configured Git hooks and helpers. The UI smoke test uses a synthetic
remote-tracking reference and does not exercise remote switching, fetch, pull,
authentication, batch failures, or conflicts through the UI.
