# ADR-0015: A stylized presentation over real data

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-27 |
| Supersedes | In part, decision 1 of [ADR-0014](0014-surface-map-textures.md): bodies without a verified map stay plain lit spheres only in the realistic style |

## Context

The project owner set the product direction on 2026-10-27. The simulation is educational and accuracy matters most, but it must also be beautiful and fun to play with. The data stays real; the effects and the beautification may be exaggerated, and the art style may be a little toon-like so the simulation is more attractive.

Until now the documents treated the look conservatively. SPEC P7 allowed display-only choices such as enlarged bodies only when marked. SPEC section 11 required every visual asset to come from a verified source. ADR-0014 rejected artistic maps for bodies without a mission mosaic, so Venus, Saturn, Uranus, Neptune, Titan, Deimos and the Uranian moons are plain lit spheres. The viewer looks realistic today: Lambert lighting, maps as published, a neutral ring color.

The rules about data (registered sources, provenance, validation, the engine boundary) are not in question. The question is how far the art layer may go, and how a learner can still tell the real from the artistic.

## Decision

1. **Two layers.** The data layer (everything the core computes and every value the UI shows) keeps all existing rules. The art layer (shaders, effects, colors, line and label styling, UI motion, artistic assets) may stylize and exaggerate within the rules of SPEC section 11.1: it changes how something looks, never where or when it is, which way it faces or is lit, or what it is.
2. **Two visual styles**, chosen by the project owner: **stylized** (the default) and **realistic**, which turns off every exaggeration and artist's impression and shows imagery as published. The active style is always visible, like the scale mode, and the viewer lists the exaggerations active in the current view (FR-REN-12, FR-REN-13). Style and scale mode are independent.
3. **Artist's impressions** for bodies without a verified map, chosen by the project owner: allowed in the stylized style, based on real imagery or published descriptions, credited with their references, labeled in the info panel, and never adding unobserved features (FR-REN-14). In the realistic style those bodies remain plain lit spheres, as ADR-0014 decided.
4. **Effects that imply a physical property** (an atmosphere halo, a corona, a comet tail, an aurora) appear only on bodies a registered source shows to have it, and keep the property's real direction where it has one. Their size and intensity are free.
5. **Visual styles live in the host.** They change only materials, shaders, effects and assets, so they need nothing new from the core and no C ABI change. An effect that needs a physical value (for example the Sun's direction from a comet) gets it from the core, as every host value does.
6. **Asset provenance splits in two.** Imagery presented as showing a real body still comes from a registered visual source (DATA_SOURCES.md, section 9). Artistic assets may be original or licensed; each is listed in the Unity project's attribution file with its license, and an artist's impression also with its references.

## Consequences

- SPEC v0.6: the purpose, principles P7 to P9, the non-goal of photorealism, UC-09, changes to FR-REN-07 and FR-REN-11, the new FR-REN-12 to FR-REN-15, NFR-11, section 11.1, and open questions OQ-09 to OQ-11.
- The current viewer already meets the realistic style. The stylized style, the style switch, the "what is exaggerated" panel and the artist's impressions are new work for the implementation plan.
- Art direction details (the palette, the strength of the toon look) are left open (OQ-09) and are settled with mockups.
- Artistic assets need an attribution path. `ATTRIBUTION.md` is generated from `data/catalog/textures.json` today, so artistic entries must reach it the same way (an extension of the catalog), not by hand edits to the output.
- Every new effect is checked against the rules of SPEC section 11.1. PlayMode tests of lighting and positions should cover both styles.

## Alternatives considered

- **Keep the realistic look only**: accurate, but the project owner wants the simulation to be attractive and fun, and plain spheres for Saturn or Neptune undersell what is known about them.
- **Stylized only, labeled but with no switch**: simpler, but a learner could never see the unembellished view, which weakens the educational aim.
- **Realistic by default, stylized on request**: hides the look the project owner wants most people to see first.
- **Stylized shading only, no artist's impressions**: keeps every surface sourced, but leaves the bodies without maps as the dullest objects in the scene.
