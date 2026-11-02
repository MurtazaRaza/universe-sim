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
| [0006](0006-default-pack-window-and-storage.md) | Default pack window and storage | Accepted |
| [0007](0007-text-kernel-values.md) | Reading values from text kernels | Accepted |
| [0008](0008-planet-positions-at-system-barycenters.md) | Mars to Pluto placed at their system barycenters in v1 | Accepted; decision 4 superseded by ADR-0010 |
| [0009](0009-depth-and-precision-across-scales.md) | Depth and precision across scales | Accepted |
| [0010](0010-moons-in-an-optional-pack.md) | The major moons in an optional pack, 2000 to 2100 | Accepted |
| [0011](0011-sky-background-and-rings.md) | Star catalog for the sky background, and planetary rings | Accepted |
| [0012](0012-lunar-orientation-from-the-binary-pck.md) | The Moon's orientation from the DE440 binary PCK | Accepted |
| [0013](0013-earth-orientation-in-the-moons-pack.md) | The Earth's orientation from a binary PCK, in the moons pack only | Accepted |
| [0014](0014-surface-map-textures.md) | Surface map textures from mission mosaics, reprojected by the pipeline | Accepted; plain spheres of decision 1 superseded in part by ADR-0015 |
| [0015](0015-stylized-presentation-over-real-data.md) | A stylized presentation over real data | Accepted |
| [0016](0016-spaceship-and-exploration-values.md) | A spaceship in the stylized style, with exploration values from the core | Accepted |

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
