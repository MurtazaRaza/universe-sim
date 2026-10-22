# ADR-0011: Star catalog for the sky background, and planetary rings

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-22 |

## Context

The project owner asked for a more interesting viewer and chose a starry sky (2026-10-22); Saturn's rings are task P6-04. Both need data that the kernels do not have: star positions, brightness and colors, and ring radii. The rules apply as for any value: registered sources, provenance with the data, no values from memory, and display choices labeled as such (FR-DATA-05).

Candidates for the stars: the Yale Bright Star Catalogue, 5th revised edition (BSC5; 9110 entries, of which 9096 stars, to about V 6.5, the naked-eye sky), the Hipparcos new reduction (118,000 stars) and Gaia DR3 (far more, saturated for the brightest stars). For rings: the IAU WGPSN Gazetteer of Planetary Nomenclature gives the boundaries of Saturn's named rings and gaps; optical depths appear in the PDS Ring-Moon Systems Node table "Vital Statistics for Saturn's Rings", a compilation whose rows cite the papers (for example French et al. 2017, Icarus 290; Colwell et al. 2009).

## Decision

1. **Stars: BSC5** from CDS (catalog V/50, `catalog.gz` and its `ReadMe`, registry entry S-CDS-BSC5, Tier 1), pinned by SHA-256 (CDS publishes no checksum list). The pipeline checks that the pinned ReadMe describes the columns at the byte ranges it reads. It keeps every star with a J2000 position and a V magnitude (9096): RA and Dec (summed exactly from the catalog's sexagesimal fields), the proper motions, V and B-V (absent for 310 stars).
2. **FK5 as ICRF.** The catalog's FK5 equinox J2000 coordinates are used as ICRF directions without a frame tie correction; the catalog gives RA to 0.1 s of time and Dec to 1 arcsec. The core moves a star linearly in its tangent plane by its proper motion from the epoch J2000.0; parallax and radial velocity are not modeled. The stars are a sky background, not astrometry.
3. **Rings: radii from the IAU Gazetteer** (S-IAU-WGPSN, Tier 1) and **optical depths from the PDS Ring-Moon Systems Node** (S-PDS-RMS, **Tier 2**, labeled as such in the pack and in the viewer), transcribed in `data/catalog/rings.json` with the page, table, row and access date. Each value is kept as the text on the page and parsed by the pipeline; a gap given as a center and a radial width becomes center plus and minus half the width, with that derivation in its locator. Saturn's main rings (D, C, B, the Cassini Division, A) and the 13 gaps the Gazetteer gives with a single width; the Huygens Gap (width given as a range), the Roche Division and the F, G and E rings are left out.
4. **Pack format 1.2** adds two optional sections, STAR and RING ([DATA_PACK.md](../formats/DATA_PACK.md)). Older readers skip them. The default pack carries both (33.4 MB, version 1.2.0, within NFR-07), and so does the moons pack.
5. **C ABI version 3** adds `usim_star_count`, `usim_star_catalog_source`, `usim_fill_stars` (directions in the display frame and the host's axes, allocation-free), `usim_ring_count` and `usim_ring_at`.
6. **Display rules**, in the Unity host and documented in its code:
   - stars: point size and intensity grow with the brightness, linearly in magnitude from V 6.5 to V -1.5; the color is a gradient over B-V from blue-white to orange-red, white without a B-V value; chosen for appearance, not photometry or colorimetry;
   - rings: a region is drawn with the middle of its optical depth range (for example 2.7 for the B ring, given as 0.4 to 5), gaps are empty, and the opacity along the line of sight is 1 - exp(-tau / |mu|) with mu the sine of the viewing elevation. The planet shadows the rings and the rings shadow the planet (the same profile, with the light's elevation). The neutral ring color is not a measured albedo.

Decided by the project owner (the starry sky, 2026-10-22) and in this ADR (sources and rules).

## Consequences

- The sky shows the real naked-eye stars in their places and colors, at any scale, and moves with proper motion over the pack's span.
- The ring opacity comes from a Tier 2 source. If a Tier 1 source of ring optical depths becomes available to the pipeline (for example the papers' tables in machine-readable form), replace S-PDS-RMS.
- Ring radii are rounded in the Gazetteer (to 10 km for the main boundaries); fine for display, not for ring science.
- Other ring systems (Jupiter, Uranus, Neptune) can be added to `rings.json` from the same Gazetteer page once optical depths are sourced; they are faint and are left out for now.

## Alternatives considered

- **Hipparcos or Gaia for the sky**: many more stars than the eye sees, larger packs (Hipparcos about 8 MB) and, for Gaia, saturated bright stars; worth it for Phase 9 (P9-02, P9-03), not for a background.
- **A sky texture** (a rendered star map image): no per-star provenance, and a fixed resolution at every zoom.
- **Ring optical depths typed as display constants**: would put physical values in code from memory, which the rules forbid.
