# Phase 6 design and evidence

## Proposed design

The launcher is a minimal edge-attached semicircle with a restrained turquoise
glow. The dashboard uses dark surfaces, muted turquoise accents, and a separate
title bar. Saved projects are accessible immediately through the sidebar;
before selecting a session, a welcome message replaces an empty terminal.
Action colors remain user-configured and are not assigned fixed meanings.
Configuration and Git dialogs retain the system theme.

This design is implemented as a proposal in response to the requested
direction, but has not been reviewed or accepted by the user.

## Automated checks

These historical checks were performed with the Phase 5 work also present in
the working tree; they do not independently verify this UI-only commit.

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

## Verification still needed

Manual checks are required for the visual design, physical pointer and keyboard
interaction, multi-monitor placement (including mixed DPI and taskbars),
position persistence, close/hide/restore, and exit confirmation with live
sessions. Existing Phase 5 limitations remain; this document does not imply
acceptance of those behaviors.
