# ADR-0010: The major moons in an optional pack, 2000 to 2100

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-22 |
| Supersedes | Decision 4 of [ADR-0008](0008-planet-positions-at-system-barycenters.md) (for the default pack) |

## Context

P6-01 adds the major moons and the planet centers from the JPL satellite ephemerides (S-NAIF-SAT). The kernels were downloaded with `tools/scripts/download-phase6-kernels.sh` (2026-10-21; all MD5-checked against NAIF's `aa_checksums.txt`). Every segment needed is SPK type 2 in J2000, so the core needs no new segment type (P6-02).

Size, measured 2026-10-22 from the segment summaries (records copied bit for bit, the moons listed below plus the planet centers and de440s.bsp):

| Window | Pack size | Of which the Mars system |
|---|---|---|
| 1849-12-26 to 2150-01-22 (the default pack's span) | 912 MB | 354 MB |
| 1950 to 2100 | 456 MB | 177 MB |
| 2000 to 2100 | 304 MB | 118 MB |

Refitting the Chebyshev records to a looser tolerance was measured on samples and does not help enough: at a 1 km position tolerance Io needs 15 MB and Phobos 77 MB over the 300-year span (2.5 times less than the source records), because the fast moons' orbits carry real high-frequency content. No refit fits the moons into the 50 MB of NFR-07, and a refit would give up the bit-for-bit provenance and VAL-01's agreement with CSPICE.

For Uranus there were two candidates: `ura184_part-3.bsp` (one file, 369 MB, solution URA184, Jacobson and Park 2025, AJ 169, 65; planetary segments from DE442) and the `ura111xl-70x` files (solution URA111, planetary segments from DE441). The download of `ura111xl-705.bsp` (Miranda) did not complete. Measured 2026-10-22 over 2000 to 2100 (20,001 epochs, positions relative to the Uranus barycenter): the two solutions differ by up to 708 km for Ariel, 366 km for Umbriel, 1299 km for Titania and 748 km for Oberon, and 1.2 km for the center of Uranus.

The viewer runs on a Mac with 8 GB of memory; the core reads a whole pack into memory.

## Decision

1. The moons go into an **optional pack**, `solar-system-moons` in `data/catalog/packs.json` (`"optional": true`), which NFR-07 allows. It is built locally from the satellite kernels and **not committed** (`.gitignore` keeps only `solar-system-<version>.uspack`). The default pack keeps the ADR-0008 placements; it cannot hold the planet centers within 50 MB.
2. Its window is **2000-01-01 to 2100-01-01 TDB** (304 MB), chosen by the project owner on 2026-10-22 from the options above.
3. Records are **copied bit for bit**; nothing is refitted.
4. The kernels are `mar099.bsp`, `jup365.bsp`, `sat441.bsp`, `ura184_part-3.bsp`, `nep097.bsp` and `plu060.bsp`. From each the pack takes **only the segments relative to its system barycenter** (the `centers` filter of a pack's ephemeris entry); the planetary segments come from `de440s.bsp`, as in the default pack.
5. Uranus uses **`ura184_part-3.bsp`**: the complete and newer solution. The `ura111xl` files are not registered.
6. The bodies (`data/catalog/bodies-moons.json`): the v1 bodies with their own centers (Mars included, so the pack has no approximate positions) and 21 moons: Phobos, Deimos, Io, Europa, Ganymede, Callisto, Mimas, Enceladus, Tethys, Dione, Rhea, Titan, Iapetus, Miranda, Ariel, Umbriel, Titania, Oberon, Triton and Charon. Their radii, GM and IAU rotation models come from the same text kernels as the default pack's (P6-03).
7. The satellite kernels are **optional source files** (`"optional": true` in `data/sources.json`): `fetch` skips them unless they are present or `fetch --optional` is given, and `build` builds optional packs only when named (`build --pack solar-system-moons`).
8. The Unity host loads the moons pack when one is present, and the default pack otherwise.

## Consequences

- With the moons pack the clock spans 2000 to 2100. Neptune (165-year period) and Pluto (248 years) get partial orbit arcs (the core samples the covered span and reports `full_revolution` false).
- Loading takes about 2.6 s in a release build (mostly the content hash) and holds about 300 MB of records in memory.
- VAL-01 and VAL-04 have moons-pack reference files (`data/reference/val01_moons_spk_states.csv`, `val04_moons_orientation.csv`), generated when the satellite kernels are present and committed; the validation tests use them when the moons pack has been built and skip otherwise. Measured 2026-10-22: 3125 states of 71 pairs agree with `spkgeo` within 2.0e-6 km, 620 moon orientations with `sxform` within 1.3e-10 rad.
- A fresh clone does not download 3.4 GB by default. Building the moons pack needs `fetch --optional` first; `fetch` reuses local copies that match NAIF's MD5.
- Larger windows remain possible as other optional packs; they would cost about 3 MB per year of coverage.

## Alternatives considered

- **Moons in the default pack** (raising NFR-07): every clone and every pack version would carry 300 MB or more through Git LFS.
- **Refitted records**: about 2.5 times smaller at 1 km tolerance, still far above 50 MB for the full span, and no longer identical to the source.
- **Longer windows** (1950 to 2100, or the full 1850 to 2150): 456 MB and 912 MB, heavy in memory next to the Unity Editor on an 8 GB machine.
- **The ura111xl set for Uranus**: Miranda's file was incomplete, and URA111 is the older solution.
