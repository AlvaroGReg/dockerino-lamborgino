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
| Configurable launches and CLI session control | Implemented | User reported successful Phase 5 tests on 2026-11-21; individual setups/results were not provided. | [Launches and sessions](specs/launches-and-sessions.md) |
| Dashboard/navigation revisions | Latest requested revision implemented, including terminal-status color indicators | Current UI smoke scripts pass for terminal lifecycle, project configuration, and inline Git workflows. Terminal status color rendering still needs visual acceptance; explicit design acceptance was not separately stated. | [Desktop experience](specs/desktop-experience.md) |
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
5. Review the full terminal dependency/license graph before distribution.
6. Run the release checklist in
   [quality and delivery](specs/quality-and-delivery.md) and record environment,
   outcomes, and any unresolved gaps here.
7. Visually verify terminal-status colors and accessible labels for terminal
   tabs, project rows, and the floating launcher, including error precedence and
   indicator clearing. See `RUN-017`, `UI-015`, and `UI-016`.

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
