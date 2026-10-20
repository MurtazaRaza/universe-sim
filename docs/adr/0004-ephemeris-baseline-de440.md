# ADR-0004: JPL DE440 as the baseline planetary ephemeris

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-20 |

## Context

Planetary positions should come from the most authoritative available source and not from our own integration. JPL Development Ephemerides (DE) are numerically integrated and fit to decades of observations (radar ranging, spacecraft tracking, lunar laser ranging). They are distributed as SPICE SPK kernels of Chebyshev polynomials.

The candidates on NAIF as of 2026-10-20:

- **DE440** (December 2020): the published, peer-reviewed solution (Park et al. 2021, AJ 161:105). It comes in `de440s.bsp` (31 MB, 1849 to 2150) and `de440.bsp` (114 MB, 1550 to 2650). NAIF publishes a matching GM kernel (`gm_de440.tpc`) and lunar orientation kernel (`moon_pa_de440_200625.bpc`).
- **DE441**: a long-span companion fit (-13200 to +17191, about 3 GB). JPL Horizons currently uses it for planets.
- **DE442** (integrated May 2024, on NAIF since February 2025): an update of DE440 that adds Uranus occultation data and more Mars and Juno ranging, mainly to improve the Uranus barycenter for the Uranus satellite ephemerides.

## Decision

1. Use **DE440** as the baseline, with **`de440s.bsp`** as the default kernel for v1 (coverage 1849-12-26 to 2150-01-22).
2. Treat the ephemeris version as configuration. The pipeline, the pack and the catalog record which kernel was used, and nothing in the code assumes a particular DE version.
3. Revisit DE442 after Phase 3, when the validation tooling can measure the differences (OQ-01).

## Consequences

- DE440 has the most complete documentation and a consistent set of companion kernels.
- Comparisons against Horizons (DE441) show small differences between ephemerides as well as our own error. VAL-02 has a provisional threshold that is set from measured differences in Phase 3. VAL-01 (against CSPICE on the identical kernel) is the strict test of our evaluator.
- Uranus is slightly less accurate than in DE442. This matters little for visualization and is a reason to revisit later.

## Alternatives considered

- **DE442 now.** Newer, but it changes mainly one barycenter and has no matching NAIF GM file of its own yet. Using DE440 first keeps the companion kernels consistent. Switching later is cheap by design.
- **DE441.** The default for Horizons and good for deep time, but about 3 GB. Kept as an optional future pack.
- **Analytic theories (VSOP87, or the JPL approximate Keplerian elements).** Small and fast, but far less accurate. They may be used only as flagged fallbacks.
- **Our own N-body integration as the source of truth.** Cannot match a solution fit to observations. It is used only for the sandbox (Phase 8).
