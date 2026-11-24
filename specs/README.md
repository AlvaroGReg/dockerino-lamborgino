# Product specifications

These specifications are the normative description of Dockerino Lamborgino's
product behavior. The implementation and its tests should conform to them.

## How to use these specs

- Requirements use stable identifiers (`CFG-*`, `RUN-*`, `GIT-*`, `UI-*`, and
  `REL-*`) so changes and verification can refer to a specific expectation.
- Where implementation status can be assessed, requirements are labeled
  **Implemented**, **Partially verified**, **Planned**, or **Open**. These labels
  describe the documented baseline, not a guarantee that every
  environment-specific acceptance check has passed. Cross-cutting policy and
  release obligations remain normative even where no single implementation
  component can be marked complete.
- If implementation, tests, and a spec disagree, do not silently assume which
  is correct. Determine whether the implementation is wrong or the requirement
  has changed; update the spec and relevant tests when behavior is intentionally
  changed.
- For a feature change: identify affected requirements, update the spec first,
  implement the change, add or update verification, then record evidence and
  status in the relevant spec and [development plan](../plan.md).
- A requirement marked **Open** or **Planned** is not a claim about current
  behavior. Historical decisions and their rationale live in
  [decisions.md](../decisions.md); they do not override these specs.

## Specification index

| Spec | Scope |
|---|---|
| [Product and project configuration](product-and-configuration.md) | Product boundaries, registration, configuration model, persistence, validation, UI/CLI editing |
| [Launches and terminal sessions](launches-and-sessions.md) | Launch execution, process sequencing, managed sessions, terminal behavior, lifecycle, CLI operations |
| [Git repository operations](git-repositories.md) | Explicit repository associations, status, branches, fetch, pull, safety rules |
| [Desktop experience](desktop-experience.md) | Dashboard, floating launcher, navigation, ordering, quick launch, application options |
| [Quality and delivery](quality-and-delivery.md) | Cross-cutting safety, platform/runtime requirements, verification, packaging, exclusions |

## Source-of-truth boundaries

- These specs define **what the product must do** and how acceptance is checked.
- `decisions.md` records **why selected approaches were preferred** and notable
  alternatives or trade-offs. It is supporting context, not a second copy of
  the requirements.
- `plan.md` records **implementation sequencing, status, and outstanding work**.
- `README.md` is the concise user-facing overview and operating guide.
- Code-level READMEs document implementation constraints that are too specific
  for the product contract.
