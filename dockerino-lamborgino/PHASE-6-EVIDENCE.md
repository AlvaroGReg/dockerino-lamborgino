# Phase 6 design and evidence

## Proposed design

The launcher is a small edge-attached semicircle with a restrained turquoise
glow, a centered marker, and an exit control between the marker and the screen
edge. The dashboard uses dark surfaces, muted turquoise accents, and a separate
title bar. Its first column lists saved projects, a subdued new-terminal action,
and an add-project action. The project-tools column stays hidden until a project
is selected. The terminal panel stays hidden until a launch or new terminal is
opened, and takes the next available space without a blank column. Active
sessions appear as tabs with a close control that stops that session, followed
by a button to open another terminal. The dashboard width animates to match the
visible columns and shrinks when the project is deselected or the last terminal
tab is closed. Action colors remain user-configured and are not assigned fixed
meanings. Configuration and Git dialogs retain the system theme.

This is the latest requested UI revision. It has not yet received manual
verification; the historic checks below refer to the prior dashboard layout.

This design was implemented in response to the requested direction. The user
subsequently reported that all requested verifications were positive; explicit
acceptance of the design was not separately stated.

## Automated checks

- Latest navigation/terminal-tab iteration: isolated Debug solution build
  succeeded with 0 warnings and 0 errors; all 39 Core and 1 Terminal tests
  passed. The build used a separate temporary output path because a running app
  instance was locking the normal Debug output.
- `dotnet test DockerinoLamborgino.sln --no-restore`: 39 Core tests and one
  Terminal test passed. These do not cover the new launcher interaction.
- Release application build succeeded with no warnings.
- An isolated UI Automation smoke check found both the dashboard and launcher
  windows and verified that the dashboard's hide button leaves the process
  running. The test process was then terminated; it had no terminal sessions.
- A second isolated smoke check verified that `--launcher-only` initially
  exposes the launcher but not the dashboard, and `dashboard open` shows the
  dashboard. This process also had no terminal sessions.
- The normal Debug build could not replace its executable because an existing
  application instance held it open. That instance was not interrupted.

## User-reported manual verification

On 2026-11-21, the user reported that all requested verifications were
completed successfully. The previously requested checks covered visual
appearance, physical interaction, multi-monitor placement, position
persistence, close/hide/restore, and exit confirmation with active sessions.
The report did not provide individual test setups or results.

Phase 5 was subsequently accepted by the user on 2026-11-21; see
[Phase 5 evidence](PHASE-5-EVIDENCE.md). That acceptance was based on the
user's report that its tests completed successfully.
