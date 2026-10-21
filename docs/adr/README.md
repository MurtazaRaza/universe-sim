# Architecture Decision Records

An ADR records one significant decision: the context, what was decided, and the consequences. ADRs are immutable once accepted. To change a decision, write a new ADR that supersedes the old one, and set the old one's status to `Superseded by ADR-NNNN`.

Write an ADR when a choice affects module boundaries, data formats, units or frames, data sources, external dependencies or validation thresholds, or when it deviates from [SPEC.md](../SPEC.md) or [ARCHITECTURE.md](../ARCHITECTURE.md).

## Index

| ADR | Title | Status |
|---|---|---|
| [0001](0001-engine-agnostic-core.md) | Engine-agnostic simulation core with thin rendering hosts | Accepted |
| [0002](0002-core-language-and-unity-integration.md) | Core in C++20 with a C ABI, consumed by Unity as a native plugin | Accepted |
| [0003](0003-units-time-and-reference-frames.md) | Units, time scale and reference frames | Accepted |
| [0004](0004-ephemeris-baseline-de440.md) | JPL DE440 as the baseline planetary ephemeris | Accepted |
| [0005](0005-data-provenance-and-storage.md) | Data provenance, pipeline and storage of large data | Accepted |

## Template

Copy this into `NNNN-short-title.md` with the next free number.

```markdown
# ADR-NNNN: Title

| | |
|---|---|
| Status | Proposed / Accepted / Superseded by ADR-NNNN |
| Date | YYYY-MM-DD |

## Context
What problem or force requires a decision? What constraints apply?

## Decision
What was decided, stated plainly.

## Consequences
What becomes easier, what becomes harder, and what follow-up work this creates.

## Alternatives considered
Each alternative and why it was not chosen.
```
