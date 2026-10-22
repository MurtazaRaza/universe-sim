# ADR-0008: Mars to Pluto placed at their system barycenters in v1

| | |
|---|---|
| Status | Accepted; decision 4 superseded by [ADR-0010](0010-moons-in-an-optional-pack.md) |
| Date | 2026-10-21 |

## Context

`de440s.bsp` gives the Sun, Mercury, Venus, the Earth and the Moon, but for Mars, Jupiter, Saturn, Uranus, Neptune and Pluto only their system barycenters (P2-03, OQ-08). The planet centers relative to their barycenters are in the JPL satellite ephemerides that NAIF publishes in `generic_kernels/spk/satellites/` (registry entry S-NAIF-SAT). FR-SIM-03 allows a fallback model when it is flagged as approximate, and FR-SIM-04 requires every state to say which model produced it.

Measured on 2026-10-21 from the NAIF satellite kernels, without downloading them in full: the DAF segment summaries and a sample of the Chebyshev records (150 records per segment, 11 points each) were read with HTTP range requests over the de440s window (1849-12-26 to 2150-01-22 TDB). The script is not part of the pipeline; these numbers are for this decision only.

| Planet | Kernel (size) | Largest distance of the center from the barycenter | Segment size trimmed to the window |
|---|---|---|---|
| Mars | `mar099.bsp` (1.1 GB) | 0.21 m | 91.2 MB |
| Jupiter | `jup365.bsp` (1.1 GB) | 220 km | 19.5 MB |
| Saturn | `sat441.bsp` (631 MB) | 311 km | 24.0 MB |
| Uranus | `ura111xl-799.bsp` (1.0 GB) | 44 km | 11.0 MB |
| Neptune | `nep097.bsp` (100 MB) | 74 km | 3.8 MB |
| Pluto | `plu060.bsp` (129 MB) | 2132 km | 14.6 MB |

Adding all six would grow the default pack from 32.7 MB to about 197 MB, above the 50 MB of NFR-07, and add that much to Git LFS storage with every pack version. Phase 6 (P6-01) selects the same kernels for the moons, whose segments come with the planet-center segments.

## Decision

1. In v1, Mars, Jupiter, Saturn, Uranus, Neptune and Pluto are **placed at their system barycenters**, and every state that uses that placement is **flagged approximate**.
2. The placement is declared in the data, not in code. The body selection (`data/catalog/bodies-v1.json`) marks these bodies with `"position": "primary"`. The pack records each one, with its proxy and a reason, in a new section APPX (format 1.1, [DATA_PACK.md](../formats/DATA_PACK.md)). The builder fails if such a body has segments of its own, so new kernels force the selection to be revisited.
3. The core links an approximated body to its proxy with a zero offset (`usim::ephemeris::EphemerisChain`) and sets `Evaluation::approximate` for every state whose path uses that link. Coverage follows the proxy.
4. Phase 6 replaces the approximations with the planet-center segments, together with the moons, and removes the `position` marks.

Decided by the project owner on 2026-10-21.

## Consequences

- The default pack stays within NFR-07 (32.7 MB, version 1.1.0). Format 1.0 readers skip APPX and report no position for these bodies instead of a wrong one.
- Positions of the six planets are off by the distances above. At display scale this is invisible for Mars and far below a planet's radius for Jupiter to Neptune. Pluto is off by about 2132 km, more than its radius, which matters once Charon is shown; Charon arrives with the satellite kernels in Phase 6, which also remove the approximation.
- Hosts must show the approximate flag (FR-SIM-04). The body info panel (P5-09) marks these values.
- VAL-01 and VAL-02 cover only bodies with their own segments; the approximation is tested with synthetic data.

## Alternatives considered

- **All six planet centers now**: exact, but about 4.1 GB of one-time downloads and a pack of about 197 MB (Mars alone 91 MB for a 0.21 m offset), above NFR-07.
- **The five outer centers now, Mars at its barycenter**: exact where the offset matters, but about 3 GB of downloads and a pack of about 106 MB, still above NFR-07.
- **An implicit rule in the core** (a planet without segments is placed at its barycenter): no format change, but the approximation would not be visible in the data, and a moon without segments could be placed wrongly by the same rule.
