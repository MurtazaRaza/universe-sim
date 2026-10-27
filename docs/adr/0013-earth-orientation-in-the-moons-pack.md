# ADR-0013: The Earth's orientation from a binary PCK, in the moons pack only

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-23 |

## Context

P6-06 asks for a high-precision Earth orientation in place of the IAU model of `pck00011.tpc`. NAIF publishes Earth binary PCKs for the terrestrial frame ITRF93 (PCK class ID 3000, a frame built into SPICE) in its `pck/` directory (`aareadme.txt` and the kernels' comments, read 2026-10-23):

| File | Coverage | Accuracy (comments) | Update |
|---|---|---|---|
| `earth_latest_high_prec.bpc` (copy of `earth_000101_yymmdd_yymmdd.bpc`) | 2000-01-01 to about three months ahead | a few microradians | at least twice a week, same name |
| `earth_620120_260806.bpc` | 1962-01-20 to 2026-08-08 | below 3 microradians | yearly |
| `earth_2026_260806_2126_predict.bpc` | 2026 to 2126 | low accuracy predict | yearly |
| `earth_1962_260806_2126_combined.bpc` (30 MB) | 1962-01-20 to 2126-11-03 | below 3 microradians to the last datum (2026-08-06) and about 10 weeks after it; then 5 to 6 milliradians | yearly; older editions move to `a_old_versions/` |

All of them give the Euler angles of ITRF93 relative to ECLIPJ2000, not J2000: precession (IAU 1976), nutation (IAU 1980) with the EOP corrections, rotation through true sidereal time and polar motion, from the JPL EOP file `latest.long`. In the long-term predict the comments hold polar motion and the nutation corrections constant and TAI - UT1R constant.

No file covers the default pack's span (1849 to 2150), and the 1962 to 2126 part of the combined file alone would add about 30 MB, past NFR-07's 50 MB. `earth_latest_high_prec.bpc` cannot be pinned (its content changes under one name) and covers only a few months of future. The moons pack's window (2000 to 2100) lies inside the combined file's coverage.

Measured with CSPICE on 2026-10-23 (20,001 epochs over 2000 to 2100): `IAU_EARTH` differs from ITRF93 in this file by up to 1.5e-3 rad (0.086 degrees) up to the last datum, and by up to 4.5e-3 rad over the predict, which is itself uncertain at 5 to 6 milliradians.

## Decision

Chosen by the project owner on 2026-10-23 from these options: the moons pack only, skipping P6-06, or both packs with the IAU model outside the file's coverage.

1. **The moons pack's Earth frame is ITRF93** from `earth_1962_260806_2126_combined.bpc` (registered as S-NAIF-EARTHPCK, Tier 1, an optional file), over the pack's window. The default pack keeps `IAU_EARTH`.
2. **No fallback**, as for the Moon (ADR-0012): the records cover the moons pack's window, which is the clock's span for that pack.
3. **Format 1.3, ORNT** (not yet released when this was decided, so amended in place rather than given a new minor version):
   - Segments may be relative to ECLIPJ2000 (base frame code 1) as well as the ICRF; the rotation from the ICRF is then R E, with E the fixed ICRF to ECLIPJ2000 rotation the core already uses.
   - A model may have no offset: an empty offset locator, the body-fixed frame equal to the PCK frame, zero angles and offset source index 0. ITRF93 needs no frame kernel. The pipeline then checks with CSPICE (`namfrm`, `frinfo`) that the frame is a built-in binary PCK frame centered on the body.
   - A segment's coverage may end up to a microsecond past its last record. Trimming sets INIT to INIT + first INTERVAL, and the Earth's record length (86,398.565 s) is not a whole number of seconds, so the kept records can end one ulp (1.9e-9 s) before the coverage the file gives, where the next segment starts. Clipping the coverage instead would leave a 2 ns gap between segments. The last record is used there, as SPICE does, within the same slack already allowed for record intervals.
4. **Provenance in the hosts**: a model with more than two segments lists its first and last segment locators and their count (the Earth has 38 in the moons pack); `usim_body_rotation_model` and the C ABI are unchanged.
5. The moons pack becomes version 1.2.0. The default pack keeps its content and version (1.3.0); only its lock file hash changed, because the lock file gained the Earth PCK.

## Consequences

- In the moons pack, the Earth's orientation follows the EOP-based ITRF93 to microradians from 2000 to the last datum, about 0.086 degrees closer than the IAU model, which matters for surface textures (P6-07) and positions on the ground. After 2026-08-06 it is a low-accuracy predict, as NAIF states; the source entry and the pack description say so.
- Measured 2026-10-23: VAL-04 for the Earth in the moons pack (144 epochs, segment and record boundaries included) agrees with CSPICE `sxform("J2000", "ITRF93")` within 3.2e-13 rad, with rates within 1.8e-11 of their size.
- The moons pack grows by 19 MB (38 segments, 36,527 daily records of degree 20) to 325 MB.
- The pinned file will move to `a_old_versions/` when NAIF publishes the next edition (yearly so far); its URL in `data/sources.json` must then be updated. A newer edition (later last datum) needs a new pin, a rebuilt moons pack and a version bump.
- The default pack's Earth stays on `IAU_EARTH` (up to 0.09 degrees from ITRF93 before 2026-08 and more after it). This is visible only close to the surface.

## Alternatives considered

- **Skip P6-06**: the IAU model everywhere. Simplest, but it gives up the microradian historical orientation that the moons pack's window can use.
- **Both packs, with `IAU_EARTH` outside 1962 to 2126**: two models silently mixed in one frame (rejected for the Moon in ADR-0012), and the default pack would exceed 50 MB unless its span were cut.
- **`earth_latest_high_prec.bpc`**: replaced twice a week under one name, so it cannot be pinned; its coverage ends three months ahead.
- **The separate historical and predict files**: the same data as the combined file in two pins.
