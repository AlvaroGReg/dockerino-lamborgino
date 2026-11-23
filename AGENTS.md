# Guidance for documentation and development

## Documentation

- Write and maintain project documentation in English. Preserve commands, paths, identifiers, and quoted output verbatim unless a change is explicitly requested.
- Before changing product behavior or scope, consult [`README.md`](README.md) for the overview, [`decisiones.md`](decisiones.md) for decisions and rationale, and [`plan.md`](plan.md) for phase status, acceptance criteria, and open questions.
- Keep these documents complementary: use the README for the concise product overview, `decisiones.md` for decision context and trade-offs, and `plan.md` for implementation sequencing. Link to existing detail rather than copying it into multiple files.
- Treat the product definition as evolving. Preserve the distinction between confirmed decisions, proposals, unresolved questions, implementation status, and historical evidence. Do not turn an assumption into a decision or claim a test, approval, or event occurred without supporting evidence.
- When documenting implementation status, check the relevant code and tests. Planned behavior is not implemented behavior.
- Keep documents that describe the same topic consistent, and update their cross-references when files or responsibilities change.

## Development references

- [`README.md`](README.md) describes the application and its build/run instructions.
- [`src/DockerinoLamborgino.Core/README.md`](src/DockerinoLamborgino.Core/README.md) documents the session registry's current behavior and constraints.

Follow the surrounding code and test conventions when implementing changes. Update the relevant development documentation when behavior or verified status changes; do not duplicate the full product specification in code-level READMEs.
