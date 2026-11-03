# ADR-0018: Atmospheres, sky colors and the sky at a surface site

| | |
|---|---|
| Status | Accepted |
| Date | 2026-11-02 |

## Context

Plan task A-07 asks for atmosphere halos in the stylized style, and B-06 for the sky seen from a surface site. Both are effects that imply a physical property, so they may appear only where a registered source shows the property (SPEC section 11.1, ADR-0015 decision 4). The look the project owner chose in A-01 also tints the halo toward the body's twilight color where the limb meets the terminator (the Earth's red, Mars's blue) and adds a sun glint on oceans (ART_DIRECTION.md section 5). A-07 asks this ADR to decide how these properties reach the viewer: in a visual catalog beside `textures.json`, or in the pack and the C ABI.

What the data and the sources give, read on 2026-11-02:

| Property | Where it is | Tier |
|---|---|---|
| Whether a body has an atmosphere | Already implied by the pack's surface conditions (ADR-0017): a positive surface pressure (Venus, the Earth, Mars, Pluto), or a reference surface that is a pressure level in the atmosphere (the four giants). Mercury and the Moon have "0" bar in the source's table. | 1 and 2, as ADR-0017 labels them |
| The Earth's twilight at the limb | NASA Earth Observatory, astronaut photograph ISS070-E-76065: "The troposphere, the lowest layer of the atmosphere, appears red and yellow." | 1 |
| Mars's twilight near the Sun | NASA Photojournal PIA00917 (Mars Pathfinder): "The blue color only becomes apparent near sunrise and sunset, when the light has to pass through the largest amount of dust." | 1 |
| Mars's daytime sky | NASA Photojournal PIA01547 (Mars Pathfinder, true color): "The brownish gray sky as it would be seen by an observer on Mars". | 1 |
| Daylight at the surface of Venus | Pieters et al. 1986, Science 234 (NASA Technical Reports Server record 19870039592): "orange-colored incident radiation resulting from interactions with the thick Venusian atmosphere". | 1 |
| The Earth's daytime sky | NASA Space Place, "Why Is the Sky Blue?": "This is why we see a blue sky most of the time." No Tier 1 statement was found in an accessible form. | 2 |
| Pluto's haze | NASA Photojournal PIA19964 (New Horizons): "Pluto's haze layer shows its blue color". A reference for the halo's display color; not used as a cue. | 1 |
| Oceans | No registered source yet gives where a body's oceans are (a land and water mask). | none |

## Decision

1. **Atmospheres come from the core.** `usim::catalog::atmosphere(const Body&)` derives the property from the surface conditions: `present` with a reference pressure or a positive surface pressure, `none` when the surface pressure is zero at the source's precision (an exosphere far thinner may exist; it is not drawn), `unknown` otherwise. There is no separate flag without provenance; the pressures carry the sources. C ABI version 7 adds `usim_body_atmosphere` with `USIM_ATMOSPHERE_UNKNOWN`, `USIM_ATMOSPHERE_NONE` and `USIM_ATMOSPHERE_PRESENT`; the CLI's `catalog` and `site` print it. No pack format change.
2. **Sky colors are sourced cues in a visual catalog.** `data/catalog/sky-cues.json` lists, per body, qualitative cues with the source's words:

   | Cue | Meaning | Used by |
   |---|---|---|
   | `daytime_sky` | The hue of the sky and of daylight at the surface by day | The surface sky (B-06) |
   | `twilight` | The hue the atmosphere takes in the light of a low Sun: near the Sun seen from the surface, and along the terminator at the limb seen from space | The halo's terminator tint (A-07) and the surface sky with a low Sun (B-06) |

   Each entry names a hue from a fixed vocabulary (`blue`, `red_and_yellow`, `brownish_gray`, `orange`), the registered source, the page or record, the access date and the verbatim sentence that states it. `usim-pipeline sky-cues` validates the entries against `data/sources.json` and writes `UniverseSim/Assets/UniverseSim/Data/SkyCues.json`, which the host reads with the citation of each cue. The hues are facts; the colors that draw them are display choices (ART_DIRECTION.md section 5). They do not go into the pack: they are qualitative, used only for drawing, and the pack holds numbers with units.
3. **The halo** (A-07) is drawn on every body the core reports with an atmosphere, in the stylized style only. Its color, thickness and intensity are display choices per body, listed in ART_DIRECTION.md section 5, and registered exaggerations. It is tinted toward the body's `twilight` hue where the limb meets the terminator, only when the body has that cue.
4. **The sky at a surface site** (B-06): near a body with an atmosphere the sky brightens by day in the `daytime_sky` hue, hides the stars, and near a low Sun takes the `twilight` hue. Its strength grows with the logarithm of the surface pressure (the reference pressure for the giants), a display mapping that keeps the order of the pressures, so Pluto's sky (about 1 Pa) stays dark while its halo still shows. A body without a `daytime_sky` cue gets a neutral gray, and the panels say that its sky color is not in the data. The sky reaches up to the top of the halo, an exaggeration registered with it. The geometry (the camera's height above the drawn body and the Sun's elevation from the core's light direction) is display geometry; the panels keep the core's values.
5. **Oceans wait for a source.** The sun glint is split off into its own task (A-16) until a registered source gives a land and water mask.

## Consequences

- SPEC FR-SIM-11 and FR-REN-16 name the atmosphere and the sky cues; FR-REN-13 already covers halos. ARCHITECTURE lists `usim_body_atmosphere`, the sky cue catalog and the new Unity components; API.md gains the function (ABI 7).
- DATA_SOURCES.md and `data/sources.json` register S-NASA-PJ-SKIES, S-NASA-EO-ISS and S-PIETERS-1986 (Tier 1) and S-NASA-SPACEPLACE (Tier 2, the Earth's daytime sky only, labeled in the panels).
- The Unity host gains the halo and the sky as style effects, listed in the "what is exaggerated" panel and absent in the realistic style; the ship and info panels show the atmosphere and the cues with their sources.
- The plan's A-07 and B-06 are done with this; A-16 holds the ocean glint.

## Alternatives considered

- **The cues in the pack and the C ABI.** Other hosts would get them for free, but the pack holds numbers with units and their provenance; a qualitative hue would need a new record kind and a format change for a fact only a renderer uses. A visual catalog, like the surface maps', keeps them with the other visual inputs.
- **An atmosphere flag in a visual catalog.** It would repeat what the pack's pressures already say and could contradict them; deriving it in the core keeps one source of truth.
- **Halo and sky colors from the cues as well.** The halo's color seen from space is not what the sky cues describe, and ART_DIRECTION.md keeps colors as display choices; only the hues that teach something (a blue Martian sunset, a brownish Martian sky) are sourced.
- **A physical sky model** (Rayleigh and Mie scattering from composition and aerosols). It would need sourced optical properties for each atmosphere and would put physics in the host, against SPEC section 3.3.
- **Drawing the sky on every body with an atmosphere at full strength.** Pluto's sky would then look like the Earth's, which would teach something false.
