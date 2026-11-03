# ADR-0017: Surface conditions and the kind of reference surface

| | |
|---|---|
| Status | Accepted |
| Date | 2026-11-02 |

## Context

ADR-0016 left the spaceship hovering: the pack had no surface temperature or pressure, and nothing said whether a body's reference surface (the ellipsoid of its IAU radii) is solid ground or, for the giant planets, a pressure level in the atmosphere. Its follow-up, plan task B-04, asks for both through the pipeline into the pack and the C ABI, Tier 1 sources where they exist and the NASA fact sheets (S-NSSDC-FACT) as labeled Tier 2 inputs otherwise, so that the ship can touch down on solid ground and stop above the others.

What the sources give, read on 2026-11-02:

| Source | Tier | What it gives |
|---|---|---|
| `pck00011.tpc` (S-NAIF-PCK11, pinned) | 1 | Before `BODY899_RADII`: "(Values are for the 1 bar pressure level.)" for Neptune. Nothing similar for Jupiter, Saturn or Uranus. |
| IAU WGCCRE 2015 report (Archinal et al. 2018), the kernel's underlying source | 1 | The reference levels of the giant planets' radii. Open access, but the publisher refuses automated downloads (a bot check, then a login redirect), and the project does not work around that. |
| NSSDC planetary fact sheet, comparison table (metric) | 2 | Mean Temperature (C) and Surface Pressure (bars) for Mercury to Neptune, the Moon and Pluto; "Unknown*" pressure for the four giants. |
| NSSDC fact sheet notes | 2 | Mean temperature is "the average temperature over the whole planet's surface (or for the gas giants at the one bar level)"; surface pressure is the pressure "at the surface of the planet"; "The surfaces of Jupiter, Saturn, Uranus, and Neptune are deep in the atmosphere and the location and pressures are not known"; the terrestrial planets are Mercury, Venus, Earth, the Moon, Mars and Pluto. |
| NSSDC Jupiter, Saturn, Uranus and Neptune fact sheets | 2 | "Equatorial radius (1 bar level) (km)": 71,492, 60,268, 25,559 and 24,764, the pack's IAU radii. |
| JPL SSD planetary physical parameters (S-JPL-SSD-PARAMS) | 1 | No temperature or pressure. |

On 2026-11-02 the project owner chose, among the options put to them, to use the NSSDC sheets as labeled Tier 2 inputs for the 1 bar level of Jupiter, Saturn and Uranus rather than wait for a manual copy of the IAU report, and to cover the ten bodies of the NSSDC comparison table (other moons keep no surface values for now).

## Decision

1. **Three quantities per body, with provenance**, in internal SI units (SPEC section 7.1 gains kelvin and pascal):

   | Field | Unit | Meaning |
   |---|---|---|
   | `mean_temperature` | K | Mean temperature at the reference surface, averaged over the body: over the solid surface, or at the reference pressure level of a body without one. Not representative of any one place (the source's note). |
   | `surface_pressure` | Pa | Atmospheric pressure at the solid surface. |
   | `reference_pressure` | Pa | The pressure level the body's reference radii refer to, for a body whose reference surface is a level in its atmosphere. |

2. **The kind of reference surface follows from them.** A body with a reference pressure has no solid reference surface (pressure level); a body with a surface pressure has a solid one; a body with neither is unknown. No body may have both. The core derives the kind (`usim::catalog::surface_kind`); there is no separate flag without provenance.
3. **Sources.** S-NSSDC-FACT becomes a Tier 2 input for these values only, labeled as such in the pack, the info panel and the ship panel: the ten mean temperatures, the six surface pressures (Mercury, Venus, Earth, the Moon, Mars, Pluto) and the 1 bar reference pressures of Jupiter, Saturn and Uranus. Neptune's reference pressure comes from the pinned kernel's note (Tier 1). The giants' "Unknown*" surface pressures stay missing. When the IAU report's wording is transcribed from a copy, it replaces the Tier 2 reference pressures.
4. **Transcription.** The values enter through `data/catalog/manual/surface-conditions.json` (P2-06 manual entries), each with the page, the table cell, the access date and a note. An entry keeps the source's text and unit (degrees Celsius, bar); the pipeline converts by definition (T(K) = t(degC) + 273.15; 1 bar = 100,000 Pa) in exact decimal arithmetic and records the conversion as the quantity's derivation. Values stay as the table gives them, rounded as it rounds them: Mercury and the Moon have "0" bar, which the panel shows as the source's value, not as a vacuum.
5. **Pack format 1.4: section SURF.** The quantities use the QNTY record layout with field codes 6 to 8 and unit codes 7 (K) and 8 (Pa), in their own section so that format 1.3 readers skip them (DATA_PACK.md section 2.3: minor versions only add sections). QNTY keeps fields 1 to 5.
6. **C ABI version 6.** `usim_body_quantity` accepts the three fields; `usim_body_surface_kind` returns the kind (`USIM_SURFACE_UNKNOWN`, `USIM_SURFACE_SOLID`, `USIM_SURFACE_PRESSURE_LEVEL`). The CLI's `site` command prints the kind and the values with their sources.
7. **The ship** (FR-REN-16):
   - Solid surface: "Descend" lands the ship. It rests on the surface as drawn: the body's sphere mesh lies inside the true ellipsoid by up to R (1 - cos(pi/128)) at its finest level (1.9 km on the Earth), so the ship is set down on the drawn facet below the site, a display correction computed from the mesh's own grid. The panel's values are those of the reference surface. The camera stays at least 1 km above the ground, like the camera rig, so float rounding does not show.
   - Pressure level: "Descend" stops at the lowest hover height above the reference level, never on it, and the panel says the body has no solid surface there.
   - Unknown: hovering as in ADR-0016.

## Consequences

- DATA_PACK.md gains SURF and the new codes (format 1.4); both implementations and the round-trip fixture change together, and the default pack becomes 1.4.0. The moons pack takes the same values when it is rebuilt (1.3.0).
- DATA_SOURCES.md describes S-NSSDC-FACT as a Tier 2 input for these values, and `data/sources.json` registers it.
- SPEC: units K and Pa (section 7.1); FR-SIM-11 and FR-REN-16 name the surface values and the landing.
- The plan's B-04 is done with this; the IAU report remains to be transcribed for the giants' Tier 1 reference level.

## Alternatives considered

- **New field codes in QNTY.** A format 1.3 reader rejects unknown field codes, so that would need format 2.0; a new section keeps old readers working.
- **A surface-kind flag in BODY.** It would carry no source; deriving the kind from sourced quantities keeps every fact traceable.
- **Degrees Celsius and bar in the pack.** Internal units are SI; display units belong to presentation (SPEC section 7.1).
- **Landing on the true ellipsoid.** The drawn sphere lies up to kilometers inside it, so the ship would float above the ground; a finer sphere everywhere would multiply the rendering cost (P6-09) and still leave a gap at the finest level.
- **Waiting for the IAU report.** The owner preferred the labeled Tier 2 values now.
