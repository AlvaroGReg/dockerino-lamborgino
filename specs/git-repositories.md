# Git repository operations specification

**Baseline:** repository association, status, branch operations, fetch, and
fast-forward-only pull are implemented. User acceptance and some environment
specific checks remain outstanding.

## 1. Repository association and visibility

- **GIT-001 — Explicit association (Implemented).** Users explicitly associate
  zero or more repository roots with a project through UI or CLI. A repository
  can be outside the project directory. Never discover repositories
  automatically.
- **GIT-002 — Repository validation (Implemented).** Validate associated paths.
  Projects with no valid repositories remain usable for non-Git features.
- **GIT-003 — Per-repository status (Implemented).** For each valid associated
  repository, show current branch and working-tree status.
- **GIT-004 — Branch list (Implemented).** Show local and remote branches as
  distinct groups with visually distinct icons/separator.

## 2. Branch switching

- **GIT-010 — Single-repository switch (Implemented).** Switch one repository
  at a time to an existing local branch. Do not provide batch branch switching
  or general-purpose branch creation.
- **GIT-011 — Remote tracking branch (Implemented).** Selecting a remote branch
  may create its corresponding local tracking branch when it does not exist;
  this selection is explicit authorization and does not require an additional
  confirmation.
- **GIT-012 — Preserve local work (Implemented).** Do not automatically stash,
  reset, commit, merge, rebase, or resolve conflicts. Let Git accept or reject
  the requested operation and show local changes/errors.
- **GIT-013 — Refresh after switch (Implemented).** Refresh repository state
  after operations without losing project selection.

## 3. Network operations

- **GIT-020 — Explicit fetch/pull only (Implemented).** Fetch and pull happen
  only after an explicit user/CLI request; they never run automatically.
- **GIT-021 — Fetch remote selection (Implemented).** Use the current branch's
  upstream remote. If there is no upstream remote, use the sole configured
  remote. If there is no remote or multiple remotes are ambiguous, report the
  problem without guessing. Resolve this independently for each repository in a
  batch.
- **GIT-022 — Pull safety (Implemented).** Pull requires an upstream and is
  fast-forward-only. If a merge or rebase would be needed, report failure
  without starting one. A failed pull may still update remote-tracking
  references through its fetch step.
- **GIT-023 — Batch result isolation (Implemented).** Batch fetch/pull continues
  after an individual repository failure and reports each repository's result.
- **GIT-024 — Credentials (Implemented/documented).** Reuse Git's configured
  user authentication mechanisms; do not store credentials in the application.
  Git terminal prompts are disabled for batch operations, so noninteractive
  credentials must be available or the error must be reported clearly. Git
  hooks and configured credential helpers are not sandboxed.

## 4. UI and CLI boundary

- **GIT-030 — UI features (Implemented).** Repository status and branch
  switching are provided in the UI.
- **GIT-031 — CLI features (Implemented).** Dockerino CLI provides batch
  `git fetch` and fast-forward-only `git pull` for repository paths explicitly
  associated with the selected project. Status and branch switching can be
  performed with Git directly in a terminal.
- **GIT-032 — Association enforcement (Implemented).** CLI Git paths must
  belong to the selected project's explicit associations.
- **GIT-033 — No extra Git mutations (Implemented).** Do not implement push,
  commit, stash, reset, automatic conflict resolution, or implicit
  synchronization.

## 5. Acceptance scenarios

1. Associate multiple repositories, including an external path, plus a
   non-Git project; verify explicit association and continued non-Git usability.
2. Verify current branch, local/remote distinction, and working-tree state.
3. Switch local branches with clean and modified trees; verify local work is not
   stashed or discarded and Git errors are visible.
4. Select a remote branch with and without a local tracking branch; verify the
   latter is created and naming/tracking conflicts are reported.
5. Exercise fetch with upstream, sole remote, no remote, and ambiguous multiple
   remotes, independently across a batch.
6. Exercise pull with upstream fast-forward, divergent history, no upstream,
   and partial batch failures. Verify no merge/rebase begins.
7. Verify CLI rejects a repository path not associated with the given project
   and continues a batch after one repository fails.
8. Verify no push, stash, commit, reset, branch creation unrelated to tracking,
   or unrequested synchronization occurs.
9. Exercise authentication success and failure using the user's Git setup.

## 6. Verification references and gaps

Automated service and Git command behavior is covered by
`tests/DockerinoLamborgino.Core.Tests/Git/`. UI workflows are covered by
`tests/UiSmoke/GitRepositoryManagementUiSmoke.ps1`. Acceptance for the user's
Git authentication environment and all edge-case combinations remains
environment-dependent and is tracked in `plan.md`.
