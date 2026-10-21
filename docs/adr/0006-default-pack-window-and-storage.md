# ADR-0006: Default pack window and storage

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-20 |

## Context

P2-11 and OQ-02 left two questions to the project owner: which time window the default data pack covers, and whether a prebuilt default pack is committed so a fresh clone can run the viewer without the pipeline. The pack format supports trimming ephemeris segments to a window ([DATA_PACK.md](../formats/DATA_PACK.md)). NFR-07 limits the default development pack to 50 MB. SPEC section 3.1 states the v1 coverage as the span of `de440s.bsp`. ADR-0005 left "commit a prebuilt default pack through Git LFS" open.

Measured on 2026-10-20: the untrimmed pack built from `de440s.bsp` (1849-12-26 to 2150-01-22 TDB) is 32,681,168 bytes; the size scales with the window's length (about 21 MB for 1900 to 2100, about 11 MB for 1950 to 2050).

## Decision

1. The default pack (`solar-system` in `data/catalog/packs.json`) covers the **full span of `de440s.bsp`**: no window is applied (`"window": null`).
2. The default pack is **committed through Git LFS** at `data/packs/solar-system-<version>.uspack` (`*.uspack` is already an LFS pattern). Other packs in `data/packs/` stay ignored. When the pack version changes, the old file is removed in the same commit.
3. The committed pack must equal what the pipeline builds from the committed lock file. A pipeline test (`test_committed_default_pack_is_current`) rebuilds it and compares the bytes.

Decided by the project owner on 2026-10-20.

## Consequences

- A fresh clone (with Git LFS) can run the core's validation tests and, from Phase 5, the Unity viewer, without Python, SpiceyPy or network access.
- The default coverage matches SPEC section 3.1 with margin for historical and future dates, at 32.7 MB, under the NFR-07 limit.
- Every pack version adds about 33 MB to LFS storage. Rebuilding with unchanged inputs gives identical bytes (P2-08), so the file changes only when sources, the pipeline's output or the format change.
- The validation tests skip (exit code 4) when the pack is an LFS pointer, and the C++ reader reports an LFS pointer with a hint to run `git lfs pull`.
- No remote exists yet; the LFS objects live in the local repository until one is added (P0-11).

## Alternatives considered

- **1900 to 2100 window** (about 21 MB): matches the VAL-02 range but would make earlier and later dates out of coverage for no strong size benefit.
- **1950 to 2050** (about 11 MB): too short for the historical dates users are likely to try.
- **Not committing the pack**: keeps the repository smaller, but every clone would need the Python environment and network access to NAIF before the viewer has data.
