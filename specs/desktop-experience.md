# Desktop experience specification

**Baseline:** latest requested dashboard/navigation revision, project/action
ordering, quick-launch, and application startup option are implemented. Some
visual/manual acceptance evidence is still outstanding.

## 1. Window roles and navigation

- **UI-001 — Floating launcher (Implemented).** Show a compact semicircular
  launcher attached to a screen edge and above other windows. It is a persistent
  way to return to the dashboard and offers application exit.
- **UI-002 — Launcher placement (Implemented; acceptance to verify).** The
  launcher can be dragged along and between monitor edges, remembers its
  location across app sessions, and avoids the taskbar by using the usable
  display work area. The default is top-center of the primary display.
- **UI-003 — Click versus drag (Implemented).** Distinguish a click that opens
  or activates the dashboard from a drag that moves the launcher, using standard
  pointer behavior.
- **UI-004 — Dashboard window (Implemented).** The dashboard is a normal
  application window: it may be covered, minimized, or hidden when another app
  takes focus. Closing/hiding the dashboard does not exit the app or stop
  sessions.
- **UI-005 — Dashboard layout (Implemented).** The dashboard provides a
  project-list column and selected-project tools column. The saved-project list
  is available from the initial view. The terminal panel appears after a launch
  or opening a new terminal; it is not shown as an empty terminal beforehand.
- **UI-006 — Terminal tabs (Implemented).** Sessions appear in closable tabs.
  Closing a tab stops that session. A final add-tab control opens a new
  terminal. Switching dashboard views or hiding the dashboard does not destroy
  active sessions.
- **UI-007 — Exit confirmation (Implemented).** Application exit is offered
  from the floating launcher, not the dashboard close button. Warn and request
  confirmation before terminating active terminal sessions.
- **UI-008 — No touch contract (Implemented).** Touch-specific interactions
  are out of scope.

## 2. Dashboard presentation

- **UI-010 — Project rows (Implemented).** Project-list rows show the project
  name and reorder handle, not the project directory path.
- **UI-011 — Persistent order (Implemented).** Users can drag projects to
  reorder them. Persist ordering independently of project configuration and
  restore it after restart.
- **UI-012 — Per-project action order (Implemented).** Users can reorder
  actions independently for each project. Ordering changes presentation only;
  it does not change configuration or execution.
- **UI-013 — Quick launch (Implemented).** An action marked `isQuickLaunch`
  shows a play shortcut in the project row. It invokes the same saved action;
  no command or execution behavior is changed.
- **UI-014 — Action meaning (Implemented).** Display action names, color values,
  and configured descriptions. Names and descriptions must convey meaning;
  colors have no fixed semantic meaning.
- **UI-015 — Project status summary (Implemented).** A project row shows the most
  severe current terminal status among sessions launched by that project:
  error takes precedence over loading. Sessions opened with **New terminal**
  have no project association and are excluded. Clear the project indicator
  when none of its sessions is loading or failed. Loading is amber and error
  is red; the indicator has an accessible tooltip.
- **UI-016 — Floating launcher status summary (Implemented).** The floating launcher
  shows the most severe current terminal status across all sessions: error
  takes precedence over loading. Clear the indicator when no session is
  loading or failed. Loading is amber and error is red; the launcher exposes
  the state through its tooltip and accessible name.

## 3. Application options and CLI activation

- **UI-020 — Windows sign-in startup (Implemented).** Provide an
  application-level option accessible from the dashboard header. When enabled,
  start the application for the current Windows user's sign-in only. Do not
  select a project or launch actions/processes automatically.
- **UI-021 — Setting scope (Implemented).** The startup option is independent
  of project configuration and applies to the current Windows user.
- **UI-022 — Dashboard activation by CLI (Implemented).** Provide a CLI
  equivalent to opening/activating the dashboard. Floating launcher placement
  and application exit do not require CLI operations.

## 4. Acceptance scenarios

1. Verify launcher default placement, all four edges, multiple monitors,
   taskbar-safe position, remembered location, and click-versus-drag behavior.
2. Verify dashboard can be covered/minimized/closed without terminating the
   app or sessions; click launcher to restore/activate it.
3. Verify initial view exposes projects and selected-project tools; no empty
   terminal appears until requested.
4. Open multiple sessions, switch/close tabs, add a terminal, hide/restore the
   dashboard, and verify only an explicitly closed session is stopped.
5. Exit with and without active sessions; verify confirmation only when
   sessions will be terminated and confirm/cancel outcomes.
6. Reorder projects and actions, restart, and verify each order persists and
   action order is scoped to its project.
7. Verify quick-launch starts exactly the configured action.
8. Toggle Windows sign-in startup and verify it starts only the app, with no
   selected project or launch action.
9. Invoke dashboard open/activation through CLI while app is running and while
   it is not running.
10. Verify project rows summarize only their own launch-created terminal
    sessions, while the floating launcher summarizes all sessions, including
    unassociated terminals. Verify error takes precedence over loading and
    indicators clear when the corresponding sessions are no longer loading or
    failed.

## 5. Verification references and gaps

Window/session smoke coverage is in
`tests/UiSmoke/WindowAndTerminalSessionsUiSmoke.ps1`. Persistence is exercised
by `tests/DockerinoLamborgino.Core.Tests/Configuration/DisplayOrderStoreTests.cs`.
The remaining multi-monitor, sign-in, and full visual acceptance checks must be
recorded as manual evidence in `plan.md` when performed.
