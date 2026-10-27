# ADR-0012: The Moon's orientation from the DE440 binary PCK

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-22 |

## Context

P6-05 replaces the Moon's IAU rotation model with the high-precision lunar orientation that accompanies DE440. NAIF publishes it in two files (registry entry S-NAIF-MOONPA, Tier 1):

- `moon_pa_de440_200625.bpc`, a binary PCK: Chebyshev polynomials of the three Euler angles of the lunar principal axes frame `MOON_PA_DE440` relative to the ICRF (PCK type 2, 8-day records of degree 9, two segments covering 1549-12-31 to 2650-01-25). Its comments say it must be used with the lunar frame kernel.
- `moon_de440_250416.tf`, the current lunar frame kernel (it replaced `moon_de440_200625.tf`, which the PCK's comments name, and `moon_pa_de440_220930.tf`; only the alias frame ID codes changed). It defines `MOON_ME_DE440_ME421`, a mean Earth/polar axis frame aligned with DE421's, as a constant rotation from `MOON_PA_DE440` given by three angles in arcseconds.

The mean Earth/polar axis (ME) frame is the one lunar maps and coordinates use; the principal axes frame differs from it by about 0.029 degrees (the frame kernel's comments). The IAU model in `pck00011.tpc` approximates the ME frame with a truncated libration series. Measured with CSPICE on 2026-10-22 (200,001 epochs over the default pack's span), `IAU_MOON` differs from `MOON_ME_DE440_ME421` by up to 1.06e-4 rad (0.0061 degrees), 4.2e-5 rad on average; over 2000 to 2040 by up to 0.0051 degrees, as the frame kernel's comments state.

The SPICE conventions were measured against CSPICE before use, on 2026-10-22: the PCK angles (phi, delta, w) give the rotation from the ICRF as [w]_3 [delta]_1 [phi]_3 (agreement to rounding, 1e-12 for the large accumulated w); for a TK frame given by ANGLES and AXES, the matrix [a1]_n1 [a2]_n2 [a3]_n3 takes vectors from the TK frame to its relative frame, so the offset is its transpose (agreement 2e-22).

## Decision

1. **The Moon's body-fixed frame is `MOON_ME_DE440_ME421`**, the DE440 principal axes from the binary PCK followed by the frame kernel's constant offset, in both packs. Other bodies keep their IAU models. The Moon's IAU elements stay in the pack (section ROTN) but no longer define its frame.
2. **No fallback.** Outside the binary PCK's coverage the Moon has no orientation (`out_of_coverage`), rather than switching to a different model. The pipeline keeps the records that cover the pack's span (its window, or without one the span of its ephemeris), so this does not happen inside the clock's limits.
3. **Records copied bit for bit**, as for SPK segments (ADR-0007 for the text values): the pack holds the PCK records overlapping the span and the frame kernel's angles as written (in its units, arcseconds), with the axes; the core evaluates both. For the default pack: one segment, 13,700 records, 3.5 MB.
4. **Pack format 1.3** adds the optional section ORNT ([DATA_PACK.md](../formats/DATA_PACK.md)), with the records in EPHD after the ephemeris records. Older readers skip it and use the IAU model. The default pack becomes version 1.3.0 and the moons pack 1.1.0.
5. **C ABI version 4** adds `usim_body_rotation_model`: the kind of model (`USIM_ROTATION_IAU`, `USIM_ROTATION_BINARY_PCK`), the frame name, the source id and the locator, so hosts show where an orientation comes from instead of assuming the IAU model.
6. **Checks**: the pipeline reads the frame chain from the frame kernel's text (cross-checked with CSPICE's kernel pool), requires the expected frame classes, center and units, and compares the offset computed from the angles with CSPICE `pxform` (within 1e-14) before writing a pack. VAL-04 compares the Moon's orientation and rate with CSPICE `sxform("J2000", "MOON_ME_DE440_ME421")`, with record boundaries among the epochs.

Decided in this ADR for task P6-05 of the plan.

## Consequences

- The Moon's surface orientation follows DE440's lunar libration; lunar textures (P6-07) align with the coordinates they are mapped in.
- Measured 2026-10-22: VAL-04 for the Moon (34 epochs, record boundaries included) agrees with CSPICE `sxform` within 1.3e-12 rad and 1.4e-12 of the rate's size in the default pack, 1.7e-12 rad in the moons pack; the pipeline's offset agrees with `pxform` within 5.4e-20.
- The default pack grows by about 3.5 MB (still within NFR-07).
- Two more files are pinned. Neither has a publisher checksum list, so `fetch` downloads them rather than trusting local copies.
- The same machinery serves the Earth (P6-06, `earth_latest_high_prec.bpc`), but that file is replaced often and covers only recent years, which needs its own decision.

## Alternatives considered

- **Keep the IAU model**: up to 1.06e-4 rad off the frame lunar coordinates use.
- **The principal axes frame `MOON_PA_DE440` without the offset**: not the frame of lunar maps; about 0.029 degrees from it.
- **Precompute the ME rotation in the pipeline** (store rotation matrices or refitted angles): loses the bit-for-bit copy of the publisher's records and adds a fitting step to validate.
- **Fall back to the IAU model outside the binary PCK's coverage**: two models silently mixed in one frame; not needed, since the records cover the pack's span.
