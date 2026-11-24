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

## Version 1.1.0: consolidation

**Scope agreed on 2026-11-24:** reliability, measured performance, repeatable
verification, and delivery hardening of the existing application. This is a
planned milestone, not a claim that the findings below have been fixed or that
release acceptance is complete.

Keep WPF/.NET, the shared Core, explicit configuration, conservative Git
operations, and per-session Job Objects. Make targeted extractions where
ownership or testability requires them; a rewrite, wholesale MVVM migration,
database migration, new user features, and a new distribution channel are not
milestone goals. WinGet remains planned under `REL-026`, but is deferred from
1.1.0; this does not waive installer safety or dependency checks.

### Review findings at `26a05dd`

These findings came from code inspection at the stated baseline. Subsequent
implementation and verification are tracked in the release ledger below; the
Git selection race was not reproduced interactively at the baseline.

| ID / priority | Finding and implementation evidence | Required regression / acceptance |
|---|---|---|
| V110-01 / P1 | Loading validates every resource in `ProjectConfigurationService.Validate`; `MainWindow.RefreshProjects` hides projects with any issue and `StartLaunch` rejects them. A missing external repository or another action's directory blocks an otherwise valid launch. | Keep structurally valid projects visible with resource diagnostics. Verify a valid selected action remains usable when an unrelated resource is absent; reject missing resources needed by the selected operation. See `CFG-025`, `GIT-002`, `RUN-007`. |
| V110-02 / P2 | `GitRepositoriesPanel.SetProject` skips a new refresh while busy, and `RefreshRepositoriesAsync` publishes results without checking the current selection. A delayed response for A can populate B's panel. | Use controlled delayed responses to switch A to B and clear selection while a query is pending. Only the current selection may receive results or errors; its refresh must not be lost. See `GIT-034`. |
| V110-03 / P2 | Closing a tab removes presentation entries in `MainWindow.CloseSessionTab_Click`, while `session.output` still locates output through those entries in `MainWindow.Launches.cs`. Independent sessions can throw; configured sessions can return empty output or reject a previously valid offset. | For both session types, retain CLI inspection/output after tab close and let an existing follower finish consistently. Separate retained history from visible tabs. See `RUN-014`, `RUN-015`, `RUN-022`. |
| V110-04 / P2 | `TerminalSessionView.Output` copies the whole accumulated buffer; `session.output` calls it on the dispatcher before applying the offset. `LocalCommands` polls every 250 ms for `follow`. | Read only the requested suffix without dispatcher-bound history access. Benchmark large histories, no-new-output polling, and multiple followers. Preserve existing output/offset semantics; no silent truncation. See `REL-035`. |

### Implementation sequence

| Order | Workstream | Exit criteria |
|---|---|---|
| 1 | Resource isolation and Git selection correctness | V110-01 and V110-02 fixed with deterministic regressions; configuration/save validation remains explicit and UI/CLI behavior agrees. |
| 2 | Session history ownership and incremental output | V110-03 and V110-04 fixed; closed-tab output and followers verified; record before/after allocations and latency under the same workloads. |
| 3 | Lifecycle and prerequisite hardening | Verify reset acceptance/outcomes, failed response delivery, stop/tab-close races, integrated descendant containment, creation failures, and shutdown. Missing selected-action prerequisites produce actionable errors without implicit installation. |
| 4 | Repeatable verification | Add a documented verification entry point and Windows CI for noninteractive build, xUnit, sequence checks, and coverage. Keep interactive checks a separate explicit release gate. Map scenarios to requirement IDs. |
| 5 | Delivery and documentation closure | Verify the existing installer and publish variants, finalize notices, reconcile implementation status, and complete the release acceptance ledger below. No WinGet submission is required for this milestone. |

Do not use a single global coverage percentage as the release criterion.
Prioritize failure paths and component boundaries; preserve the existing Core
coverage while extending CLI, terminal, and application integration coverage.
Choose and record performance workloads and budgets before accepting the
optimization, rather than claiming improvement from code changes alone.

### Review verification baseline

On 2026-11-24 at `26a05dd`, in the local Windows development environment:

- `dotnet build DockerinoLamborgino.sln --no-restore -c Release` succeeded with
  zero warnings and errors.
- `dotnet test DockerinoLamborgino.sln --no-restore -c Release` passed 69 Core
  and 1 Terminal tests, with no failures or skips.
- `dotnet run --project tests/DockerinoLamborgino.App.SequenceChecks/DockerinoLamborgino.App.SequenceChecks.csproj --no-restore -c Release`
  passed.
- A second xUnit run with `--collect "Code Coverage;Format=Cobertura"` also
  passed. The local report is under `artifacts/audit-coverage/` (not a
  versioned or durable CI artifact).

| Measured assembly | Line coverage | Branch coverage |
|---|---|---|
| Core | 87.6% | 81.8% |
| CLI | 64.6% | 55.3% |
| Terminal | 70.4% | 47.1% |
| App/WPF | Not included | Not included |

These are assembly-level results from xUnit only, not whole-product coverage.
The collector also includes test assemblies; their coverage must not inflate a
product total. Sequence, UI, and installer checks are outside this measurement.
No new interactive UI, installation, upgrade, clean-machine, or performance
acceptance was performed during this review. Earlier evidence below remains
historical and is not superseded by the newer test count.

### Release acceptance ledger

Each gate remains open until its evidence records the tested revision,
environment/tool versions, exact command or scenario, outcome, and artifact
location. Record any unresolved gap and explicit release disposition; a
passing unit suite does not close an integrated or manual gate.

Local verification on 2026-11-24 used Windows 10.0.26300 x64 and .NET SDK
10.0.404 against base `26a05dd` with uncommitted changes.
`pwsh -NoProfile -File scripts/verify-windows.ps1` passed: 75 Core, 10 Terminal,
and 1 local command server tests, plus command-sequence and Git selection
checks. The run's transcript, TRX and coverage files are under
`artifacts/verification/20261124-124017-5580f745722347e8b318f4eda08e1cd7/`.
A second run of the same command passed with the same counts and checks;
its evidence is at
`artifacts/verification/20261124-124341-363187a5ab9b4c70a406d2200ec24f18/`.
Both runs used an uncommitted working tree and predate the final terminal
reader correction. On 2026-11-24, `tests/UiSmoke/WindowAndTerminalSessionsUiSmoke.ps1`
subsequently passed on this Windows desktop after correcting a closed-tab output
completion hang. It checked inspection, output, offsets and followers for both
configured and independent sessions; it does not establish visual color
acceptance or OS-level descendant containment.
After the reader correction, `pwsh -NoProfile -File
tests/UiSmoke/ResetIntegration.ps1` also passed real contextual and external
reset, acceptance IDs, inspection, replay, stale-ID rejection, and an
independent session. It does not prove every reset race.

After the reader correction, the same verification command passed again with
76 Core, 14 Terminal and 1 local command server tests, plus both standalone
checks. Evidence:
`artifacts/verification/20261124-125214-0b52bc8ac72843c79d5cd8eb39387c05/`.
The tested base is `26a05dd` with uncommitted changes.
Subsequently, loopback HTTP 401 tests passed for noninteractive Git fetch and
pull; they do not establish SSH or credential-helper behavior.
`dotnet test tests/DockerinoLamborgino.Terminal.Tests/DockerinoLamborgino.Terminal.Tests.csproj --no-restore --verbosity quiet`
then passed 15 tests, including real ConPTY sessions with child and grandchild
processes: stopping one tree left the other running, and disposing the second
owner stopped its tree. `tests/UiSmoke/IsolatedApplicationShutdownSmoke.ps1`
also passed on the local desktop: confirmed launcher Exit closed an isolated
application and its three-process tree while an independent sentinel survived.
It does not verify all shutdown races or failure injection. The test moves the
cursor and interacts with the desktop; run it only when that is acceptable.
After the version, startup-command, Git and containment test additions,
`scripts/verify-windows.ps1` passed again: 86 Core, 15 Terminal and 1 server
tests, plus both standalone checks. Evidence:
`artifacts/verification/20261124-131727-73dd68ed4902421c8b705168cfa31ed9/`.

On the same environment, `scripts/build-installer.ps1 -Version 1.1.0
-OutputRoot <temporary-directory>` published the self-contained app and CLI and
compiled the real installer with Inno Setup 7.1.1. The compiler listed the
application, CLI, project notices and restored runtime-pack license files as
inputs. Output:
`%TEMP%\opencode\dockerino-110-installer-verify-14b59b3202b945d9a2d835e52a5d1843`.
The published CLI's `--version` printed `1.1.0.0`. This verifies building and
CLI startup, not installing, upgrading, extraction, or license completeness.
After the terminal reader correction, the installer build also succeeded at
`%TEMP%\opencode\dockerino-110-final-0c524f2167104e81be75dc32beca418e`;
the single-file app publish emitted the `IL3000` warning described below.

The framework-dependent single-file `dotnet publish` command from `README.md`
also succeeded, to
`%TEMP%\opencode\dockerino-110-framework-verify-475618dbce3247c687c65ce6bb235bfe`.
It emitted `IL3000` for the `Assembly.Location` fallback. That fallback has
since been isolated to the dotnet-hosted case with a targeted suppression;
both single-file publish variants subsequently succeeded with `IL3000`
treated as an error, and command construction has isolated tests. Execution
and actual sign-in startup of those newer builds have not been verified.

| Gate | Current evidence / gap | Status |
|---|---|---|
| Functional regressions (`CFG-025`, `GIT-002`, `GIT-034`, `RUN-014`, `RUN-022`) | Operation-scoped resource validation and selection-consistent Git refresh have deterministic checks. The closed-tab desktop smoke passed for both session types and followers; broader integrated launch acceptance remains open. | Open |
| Output performance (`REL-035`) | [Output benchmark](tests/SessionOutputBenchmark/RESULTS.md) records provisional budgets and comparable measurements. [Idle terminal benchmark](tests/DrainingTerminalIdleBenchmark/README.md) measures 60 ConPTY sessions, but does not isolate reader cost, child CPU, UI or end-to-end performance. | Open |
| Lifecycle and prerequisites (`RUN-007`, `RUN-013`, `RUN-019`, `RUN-030`, `RUN-031`) | Selected-action directory checks and missing PowerShell diagnostics are implemented. New Core reset race and disconnected pipe handler tests do not close integrated races, descendants, shutdown, or missing-tool UI checks. | Open |
| Repeatable verification (`REL-030`, `REL-034`) | `scripts/verify-windows.ps1` and Windows CI workflow added; latest local results at `artifacts/verification/20261124-131727-73dd68ed4902421c8b705168cfa31ed9/`. An actual CI run and release evidence remain pending. | Open |
| Desktop acceptance (`UI-*`, `RUN-017`, `RUN-018`) | Existing smoke scripts and historical reports; outstanding interactive/visual, multi-monitor, and sign-in checks below. | Open |
| Packaging (`REL-022`, `REL-025`, `REL-027`) | Installer close guidance now explains the detached-Docker boundary. Clean Windows x64, dependencies, PATH, both publish variants, safe interactive/unattended upgrades, uninstall, and persistence remain to verify. | Open |
| Dependency notices (`REL-003`) | Runtime-pack notices are collected; full native/transitive review and inspection of the release notice bundle remain open in [third-party notices](THIRD-PARTY-NOTICES.md). | Open |
| Documentation and acceptance (`REL-031`, `REL-032`) | Milestone documented; final implementation reconciliation and explicit release acceptance pending. | Open |

## Baseline status before consolidation

| Area | Implementation | Verification / acceptance | Specification |
|---|---|---|---|
| Terminal integration | Implemented | Phase 1 manual acceptance reported complete on 2026-11-19: prompt, physical keyboard input, resize, and marker file. | [Launches and sessions](specs/launches-and-sessions.md) |
| WPF skeleton and shared backend | Implemented | Skeleton accepted; subsequent UX requirements are defined in the desktop specification. | [Desktop experience](specs/desktop-experience.md) |
| Project registration/configuration | Implemented | Automated verification and user acceptance reported. | [Product/configuration](specs/product-and-configuration.md) |
| Git associations and operations | Implemented, including resource isolation and selection-refresh corrections | Deterministic regressions added; user acceptance and some edge-case verification are outstanding. | [Git](specs/git-repositories.md) |
| Configurable launches and CLI session control | Incremental retained output implemented | Closed-tab output and followers passed the desktop smoke; broader visual/manual acceptance remains open. | [Launches and sessions](specs/launches-and-sessions.md) |
| Dashboard/navigation revisions | Latest requested revision implemented, including terminal-status color indicators | The terminal lifecycle smoke passed on 2026-11-24; color rendering still needs visual acceptance, and explicit design acceptance was not separately stated. | [Desktop experience](specs/desktop-experience.md) |
| Delivery hardening | Partial | Release checklist and environment-specific checks remain. | [Quality and delivery](specs/quality-and-delivery.md) |

## Planned UI improvements (outside version 1.1.0)

The following requested behavior is documented for future implementation and
does not expand the 1.1.0 consolidation scope:

- Dashboard terminal controls and project status: `RUN-034` and `UI-017`.
- Collapsible dashboard columns: `UI-018`.
- Direct project selection, unsaved-change handling, and Actions-first
  configuration navigation: `CFG-026` and `CFG-027`.

## Outstanding checks and historical evidence

The 1.1.0 workstreams and release ledger above govern milestone closure. This
list retains earlier checks and qualified reports; WinGet-specific work is
future delivery work, not an additional 1.1.0 feature.

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
10. The corrected terminal UI smoke passed on an interactive desktop on
      2026-11-24; visual color acceptance remains separate.
11. Complete Windows x64 installer hardening and, separately, future WinGet distribution
     (`REL-025`–`REL-027`): package the app and CLI with .NET, expose the CLI,
     declare and verify PowerShell 7 and Git dependencies, and support safe
     upgrades. Finish the initial Inno Setup per-user installer, add GitHub Actions
     artifact builds, publish versioned GitHub releases, and initially submit
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
