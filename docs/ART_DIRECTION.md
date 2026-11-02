# Art Direction

| | |
|---|---|
| Status | Decided (A-01, OQ-09) |
| Date | 2026-10-27 |
| Decided by | The project owner, from rendered mockups |
| Related | [SPEC.md section 11.1](SPEC.md#111-art-direction), [ADR-0015](adr/0015-stylized-presentation-over-real-data.md), [IMPLEMENTATION_PLAN.md Track A](IMPLEMENTATION_PLAN.md#track-a-art-direction-alongside-phases-7-to-9) |

This document fixes the look of the **stylized** style (FR-REN-12) for Track A. The rules of SPEC section 11.1 apply on top of everything here: the art changes how things look, never where or when they are, which way they face or are lit, or what they are. The **realistic** style keeps today's viewer: Lambert light, maps as published, neutral rings, plain stars.

## 1. The look: Adventure

Halfway between the real Solar System and a polished console game (the project owner's reference: Astro Bot on the PlayStation 5). Surfaces are smooth and clay-like with vivid but recognizable color, a satin finish and a bright day side. Strong rim light shapes every body, a cool rim keeps the night side readable, and atmospheres are thick, luminous and colored at twilight. The sky is deep indigo with sparkling stars, and bloom makes atmospheres, stars and the Sun glow. There are no light bands, no outlines and no ink lines.

| Subject | Realistic | Adventure |
|---|---|---|
| Earth | ![Earth, realistic](art/earth-realistic.jpg) | ![Earth, Adventure](art/earth-adventure.jpg) |
| Moon | ![Moon, realistic](art/moon-realistic.jpg) | ![Moon, Adventure](art/moon-adventure.jpg) |
| Mars | ![Mars, realistic](art/mars-realistic.jpg) | ![Mars, Adventure](art/mars-adventure.jpg) |
| Jupiter | ![Jupiter, realistic](art/jupiter-realistic.jpg) | ![Jupiter, Adventure](art/jupiter-adventure.jpg) |
| Saturn's rings | ![Saturn's rings, realistic](art/saturn-rings-realistic.jpg) | ![Saturn's rings, Adventure](art/saturn-rings-adventure.jpg) |

The images are renders of the viewer at 2026-10-21 12:00 UTC with the moons pack. Saturn is a plain sphere because it has no verified map; its look comes with the artist's impressions (A-11).

**How it was chosen.** Three rounds of mockups were rendered from the real scene and compared with the realistic look:

| Round | Candidates | Outcome |
|---|---|---|
| 1 | Soft bands, bold cel shading with an outline, smooth light with strong glow | Too tame |
| 2 | Comic ink, gouache brush patches, flat poster | Not the wanted direction |
| 3 | Three steps from real toward a polished game look | The middle step, Adventure, chosen |

**Where it lives.** The style switch is `VisualStyleSettings` (A-02). Body shading and the map treatment are the stylized variant of `BodyLit` with `SurfaceMapTreatment.hlsl`, driven by the `StylizedBodyShading` and `SurfaceMapTreatment` components (A-05, A-06); the camera is `StylizedCamera` (A-08). Each component holds this document's values as its defaults and lists what it exaggerates in the viewer's "what is exaggerated" panel (A-03).

The mockups come from `Tests/PlayMode/ArtDirectionMockups.cs`, with the shaders in `Assets/UniverseSim/Mockups/`. They are the reference implementation for A-05 to A-09 and are removed when those tasks are done. To render them again (the Editor must not have the project open):

```
USIM_MOCKUP_DIR=<folder> /Applications/Unity/Hub/Editor/6000.5.1f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath UniverseSim -runTests -testPlatform PlayMode \
  -testFilter UniverseSim.Unity.Tests.PlayMode.ArtDirectionMockups -testResults /tmp/mockups.xml
```

## 2. Palette

Every value is a display choice, given as a Unity color (sRGB components, as the mockups set them); Unity converts it to linear for the shaders, as it does any material color. The sky is the camera's background color.

| Element | Color | Use |
|---|---|---|
| Sky | (0.02, 0.02, 0.06) | Camera background, deep indigo |
| Light | (1.10, 1.04, 0.95) | Tint of the brightest day side, warm |
| Shade | (0.75, 0.80, 1.05) | Tint of the day side next to the terminator, cool |
| Night | (0.40, 0.45, 1.00) | Tint of the night side |
| Night rim | (0.35, 0.65, 1.00) | Rim along the night limb |
| Rings, sparse | (0.70, 0.58, 0.45) | Ring regions of low optical depth |
| Rings, dense | (1.00, 0.90, 0.68) | Ring regions of optical depth 2 and above |

Atmosphere colors (starting values from the mockups; section 5 says where the bodies and twilight hues come from):

| Body | Outer | At the limb | Twilight |
|---|---|---|---|
| Earth | (0.20, 0.45, 1.00) | (0.55, 0.80, 1.00) | (1.00, 0.50, 0.25), red |
| Mars | (0.85, 0.80, 0.80) | (0.95, 0.90, 0.85) | (0.45, 0.60, 1.00), blue |
| Jupiter | (1.00, 0.85, 0.65) | (1.00, 0.95, 0.85) | None |
| Saturn | (1.00, 0.88, 0.62) | (1.00, 0.95, 0.85) | None |

## 3. Body shading (A-05)

With n the surface normal, l the direction to the light from the core's snapshot and v the direction to the camera:

| Part | Rule |
|---|---|
| Day side | Brightness 1 - (1 - n·l)^2.2: zero at the terminator like Lambert, but brighter across the disc. The tint goes from Shade at the terminator to Light at the subsolar point along the same curve. |
| Terminator | Where n·l = 0, antialiased over one pixel. Nothing on the day side reaches past it, and nothing on the night side reaches up to it. |
| Night side | The map at 12 % brightness, tinted Night. |
| Satin sheen | Blinn-Phong, exponent 8, strength 0.2, in a bright shade of the surface's own color (saturate(1.8 c + 0.08)); day side only, fading in over n·l from 0 to 0.15. |
| Sun glint | Exponent 450, strength 1.8, white, where the map shows open water (dark and blue). Only on bodies that a registered source shows to have oceans (section 5). |
| Day rim | (1 - n·v)^2.5 times 0.55, 70 % of the way from white to a bright shade of the surface color (saturate(1.6 c + 0.25)); fades in over n·l from 0 to 0.25. |
| Night rim | (1 - n·v)^3.5 times 0.3, in Night rim; fades in over n·l from 0 to -0.3, so it stops short of the terminator. |
| Brightness cap | A surface's color is scaled down as a whole to a largest channel of 0.95. Its hue holds, and no surface reaches the bloom threshold (section 7), so an airless body never gets a haze that could read as an atmosphere. |
| Rings' shadow | As today: the day-side brightness is dimmed by the rings' transmission. |

No bands, outlines or ink. The ambient term stays as in `BodyLit` (0.015).

## 4. Surface maps (A-06)

| Step | Rule |
|---|---|
| Smoothing | A Kuwahara filter: of four 4 x 4 neighborhoods that meet at the texel, the mean of the one that varies least, with the samples spaced half a texel of the mip read. That mip is one coarser than the map's size on screen needs and never finer than mip 1: at the mockups' distance (about one texel per pixel) that is mip 1, as in the mockups, and farther out the patches stay about 3 pixels wide instead of shimmering. |
| Saturation | x1.6 about the Rec. 709 luminance. |
| Contrast | Luminance only, power law 0.9 about 0.4: shadows lift and highlights soften, and each texel keeps its hue and its share of each channel. |
| Hue tolerance | The average hue of each treated map stays within **5 degrees** of the published map's. The average hue is the HSV hue of the area-weighted mean linear color, converted to sRGB. Maps whose mean color has a chroma (largest minus smallest sRGB channel) below 0.02 are grey: they have no meaningful hue and must stay grey. |

The tolerance was measured on 2026-10-27 by emulating the treatment over all 19 maps. The largest change was 3.4 degrees (Io, the most colorful map), every other map changed by 1.5 degrees or less, and the 11 monochrome maps stayed grey. The light and shade tints of section 3 are lighting, not map treatment, and the test does not include them.

## 5. Atmospheres and other physical cues (A-07)

| Part | Rule |
|---|---|
| Shell | 12 % of the radius on the Earth. The other bodies use the same profile at a fraction of the thickness and intensity, set per body as a display choice (in the mockups 0.5 for Mars, Jupiter and Saturn, with intensities 0.35, 0.55 and 0.45). |
| Profile | Computed from the view ray. The glow peaks at the limb, falls off over the shell with exponent 2.2 at intensity 2.1, and reaches onto the disc as b^16, where b is the ray's closest approach in body radii. |
| Color | From the limb color to the outer color across the shell. Where the limb meets the terminator, 60 % toward the twilight color. |
| Sunlit side | Brightest toward the light: smoothstep(-0.25, 0.45) of the limb direction against the light direction. |

**Sources.** Three cues imply a physical property, and each needs a registered source before the viewer shows it (SPEC section 11.1, ADR-0015 decision 4):

- an atmosphere (which bodies get a shell)
- the twilight hue (the Earth's red, Mars's blue)
- oceans (which bodies get the sun glint)

A-07's ADR decides how these properties travel: in a visual catalog beside `textures.json`, or in the pack and the C ABI. The colors themselves are display choices.

## 6. Stars and rings (A-09)

| Part | Rule |
|---|---|
| Stars | The ADR-0011 mapping of V and B-V, then exposure x1.9 and size x1.5. Stars with a display intensity above 0.9 get a four-point sparkle up to 5 star radii long, at strength 0.8, growing with intensity, so a brighter star never looks fainter. |
| Rings | The same radii, profile, slant opacity, lit and unlit sides and planet shadow. Color goes from Rings, sparse to Rings, dense as the optical depth grows to 2, opacity gain x1.5 and brightness x1.2. Both mappings rise with optical depth. |

## 7. Camera (A-08)

| Part | Stylized | Realistic |
|---|---|---|
| Bloom | Intensity 0.9, threshold 1.33 (gamma, about 1.9 linear), scatter 0.7, high-quality filtering | Off |
| Color adjustments | Saturation +15, contrast +8 | Off |
| Vignette | 0.3, smoothness 0.5 | Off |
| Tonemapping | Neutral | Neutral |
| Background | Sky (section 2) | Black |

URP's bloom has a fixed soft knee of half the threshold. The threshold of about 1.9 linear therefore keeps everything below 0.95 out of the bloom (the brightness cap of section 3), so only atmospheres, stars and the Sun bloom. Today's scene still carries the URP template's volume (bloom 0.25, vignette 0.2, Neutral tonemapping); A-08 turns its bloom and vignette off in the realistic style. The Sun's corona and glare are A-08's own work and follow this palette.

## 8. Effects in priority order

A-02 (the style switch), A-03 (the exaggeration register) and A-04 (attribution for artistic assets) come first, as the plan says. Then:

1. Body shading (A-05): the largest change in every view.
2. Surface map treatment (A-06).
3. Camera: bloom, grading, vignette and the indigo sky (A-08, without the corona).
4. Atmospheres with twilight colors (A-07), after the ADR on how their source data travels.
5. Stars with sparkles and the ring palette (A-09).
6. The Sun's corona and glare (A-08).
7. Sun glint on oceans, once the ocean property is sourced (with A-07).
8. Orbit lines and labels in this palette (A-10).
9. Interaction feel (A-12).

## 9. Checks

| Check | Where |
|---|---|
| The terminator is within a pixel of where the light puts it, in both styles | `BodyShadingTests` (A-05) |
| Each map's average hue is within 5 degrees, and grey maps stay grey | `SurfaceMapTreatmentTests` (A-06) |
| No surface exceeds 0.95, so no airless body blooms | `BodyShadingTests` (A-05) |
| Star brightness and color order and ring density order are kept | A-09 tests |
| Atmospheres, twilight colors and the glint appear only where a registered source shows the property | A-07 |
| Every exaggeration above is in the "what is exaggerated" register | `ExaggerationTests` (A-03) |

## 10. The spaceship (Track B)

A small, friendly craft in the same Adventure look ([ADR-0016](adr/0016-spaceship-and-exploration-values.md)): a rounded cream hull with a coral nose and wings, a glassy blue canopy, twin engines with cyan nozzles that glow and bloom with thrust. It is generated by code (`ShipMesh`) and drawn by the `Ship` shader with the bodies' stylized shading rules (section 3): the same day curve from Shade to Light, a satin sheen, a rim that keeps it readable on the night side, and a night lift. It is lit by the core's light direction at the body it is near and dims in that body's shadow. It flies a short way ahead of the camera and always takes about a sixth of the view's width, so its size never stands for a real one.

| Part | Color (Unity, sRGB) |
|---|---|
| Hull | (0.93, 0.91, 0.86) |
| Accent (nose, wings, fin) | (1.00, 0.52, 0.30) |
| Canopy | (0.25, 0.70, 1.00) |
| Engine trim | (0.30, 0.33, 0.42) |
| Nozzle glow | (0.45, 0.85, 1.00), emitted at up to 4x, so it blooms |

The ship is listed in the "what is exaggerated" panel whenever it is out ("Spaceship", "Flights"), and it does not exist in the realistic style.
