# Development plan and status

The product contract and acceptance scenarios live in the
[specifications](specs/README.md). This file tracks implementation sequencing,
verified status, and remaining work; it is not a second requirements document.

## Status vocabulary

- **Implemented:** the behavior is present in the code.
- **Verified:** the stated checks have been performed and evidence is recorded.
- **Accepted:** the user has explicitly accepted the result.
- **Open:** work or evidence remains; do not treat the requirement as complete.

Implementation status is not the same as verification or user acceptance.
Update this file only when supported by code, test results, or recorded user
feedback.

## Baseline status

| Area | Implementation | Verification / acceptance | Specification |
|---|---|---|---|
| Terminal integration | Implemented | Phase 1 manual acceptance reported complete on 2026-11-19: prompt, physical keyboard input, resize, and marker file. | [Launches and sessions](specs/launches-and-sessions.md) |
| WPF skeleton and shared backend | Implemented | Skeleton accepted; final UX is defined separately below. | [Desktop experience](specs/desktop-experience.md) |
| Project registration/configuration | Implemented | Automated verification and user acceptance reported. | [Product/configuration](specs/product-and-configuration.md) |
| Git associations and operations | Implemented | User acceptance and some edge-case verification remain outstanding. | [Git](specs/git-repositories.md) |
| Configurable launches and CLI session control | Implemented, including red indicators for nonzero configured-command results | A UI smoke case covers configured command failure status; execution and visual/manual acceptance remain open. | [Launches and sessions](specs/launches-and-sessions.md) |
| Dashboard/navigation revisions | Latest requested revision implemented, including terminal-status color indicators | The terminal lifecycle smoke script's tab-count assertion has been corrected but the script still needs an interactive desktop run. Terminal status color rendering still needs visual acceptance; explicit design acceptance was not separately stated. | [Desktop experience](specs/desktop-experience.md) |
| Delivery hardening | Partial | Release checklist and environment-specific checks remain. | [Quality and delivery](specs/quality-and-delivery.md) |

## Remaining work

1. Complete and record Git acceptance cases, especially authentication outcomes,
   ambiguous remotes, local modifications, divergent pulls, and partial batch
   failures. See `GIT-*`.
2. Complete multi-monitor launcher and Windows sign-in startup checks. See
   `UI-*`.
3. Complete process-tree containment and application-shutdown verification in
   the integrated app, including representative descendants and creation
   failures. See `RUN-*`.
4. Verify and, where needed, implement launch preflight for missing selected
   action directories, commands, shell, terminal components, and required tools.
   See `RUN-007`.
5. Complete the third-party dependency/license graph and notices before
   distribution. The partial review and unresolved provenance and notice
   questions are recorded in `THIRD-PARTY-NOTICES.md`.
6. Run the release checklist in
   [quality and delivery](specs/quality-and-delivery.md) and record environment,
   outcomes, and any unresolved gaps here.
7. Visually verify terminal-status colors and accessible labels for terminal
   tabs, project rows, and the floating launcher, including error precedence and
   indicator clearing. See `RUN-017`, `UI-015`, and `UI-016`.
8. Complete verification of the asynchronous reset contract. Core tests cover reservation, inspection,
   replay, failures, retries, stop races, and asynchronous sequence failure
   before readiness; CLI tests cover syntax, pipe requests and an unavailable
   app. On 2026-11-24, `dotnet test DockerinoLamborgino.sln --no-restore`
   passed 66 Core and 1 Terminal tests, and the standalone sequence checks
   passed. On 2026-11-24, `ResetIntegration.ps1` passed twice with the real app,
   CLI and ConPTY: self-reset and external reset, replay, inspection, stale ID
   rejection and an independent session. It does not deterministically verify
   acceptance before completion or failed response delivery. The user reported
   expected reset behavior on 2026-11-24 after the manual testing instructions;
   the exact scenarios exercised were not specified. Remaining contract and
   stop/tab-close race verification is still open. See `RUN-019`.
 9. Verify direct terminal interaction clears only that session's configured
   command error indicator, including the project and launcher summaries.
    See `RUN-018`.
 10. Run the corrected terminal UI smoke test on an interactive desktop before
     relying on its configured-command failure and lifecycle coverage.
 11. Implement the planned Windows x64 installer and WinGet distribution
     (`REL-025`–`REL-027`): package the app and CLI with .NET, expose the CLI,
     declare and verify PowerShell 7 and Git dependencies, and support safe
     upgrades. Implement the Inno Setup per-user installer and GitHub Actions
     artifact build, publish versioned GitHub releases, and initially submit
     WinGet manifest updates manually. Check acceptance of the selected package
     ID; test dependency detection for absent, existing and portable installs,
     executable availability, interactive and unattended running-app upgrade
     handling, and Windows trust warnings without signing. Verify installation,
     upgrades, uninstall and persistence before release. WinGet dependency
     support has been researched, but end-to-end behavior has not been tested.
     `scripts/publish-windows.ps1` prepares separate versioned self-contained
     app and CLI outputs. An initial Inno Setup installer and build script
     compile, but installation, upgrades, uninstall, PATH handling, and
     running-app protection have not yet been verified. WinGet packaging and
     release automation are not yet implemented. On 2026-11-24, both publish commands succeeded
     for a test version, the published CLI printed usage, and the solution
     tests passed (66 Core, 1 Terminal). The app publish emitted IL3000 for
     `WindowsStartupSetting.cs` and needs review and single-file verification.
     The user subsequently reported a successful fresh installation and
     provided screenshots showing the installed app, existing projects,
     `dockerino --help` in an app terminal and an external PowerShell, and a
     successful `dockerino dashboard open` response. The screenshots do not
     establish a clean Windows environment or verification of the remaining
     installation, dependency, upgrade and uninstall cases.
     The user also reported successful uninstall and reinstall; the exact
     persistence and PATH checks performed were not specified.
     The user reported that without PowerShell 7 an installed app's terminal
     fails to start; a screenshot shows the generic message "Terminal failed:
     Could not create the terminal process." Missing-shell detection and an
     actionable error remain open under `RUN-007`. This does not verify WinGet
     dependency installation.

## Change workflow

For any product behavior change:

1. Identify the affected requirement IDs and update the relevant spec before
   implementation.
2. Implement against those requirements and update automated/manual acceptance
   checks.
3. Verify the change; record actual results and remaining gaps here.
4. Update `decisions.md` only when the rationale, selected approach, or
   meaningful alternatives change.
5. Keep `README.md` and code-level READMEs consistent with user-facing and
   implementation-level behavior.

## Historical phase mapping

The earlier phase plan is summarized in the baseline table above. Detailed
requirements formerly duplicated in phase descriptions and MVP checklists now
live in the specs; keep phase/status notes here concise and evidence-based.
