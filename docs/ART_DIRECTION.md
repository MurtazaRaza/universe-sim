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

**Where it lives.** The style switch is `VisualStyleSettings` (A-02). Each part of the look is an effect component that holds this document's values as its defaults and lists what it exaggerates in the viewer's "what is exaggerated" panel (A-03):

| Part | Component | Drawn by |
|---|---|---|
| Body shading (section 3) | `StylizedBodyShading` (A-05) | The stylized variant of `BodyLit` |
| Surface maps (section 4) | `SurfaceMapTreatment` (A-06) | `BodyLit` with `SurfaceMapTreatment.hlsl` |
| Stars (section 6) | `StylizedStars` (A-09) | The stylized variant of `StarField` |
| Rings (section 6) | `StylizedRings` (A-09) | The stylized variant of `Rings` |
| Camera (section 7) | `StylizedCamera` (A-08) | A post-processing volume made at runtime |
| The Sun (section 7) | `StylizedSun` (A-08) | The stylized variant of `Star`, and `SunGlow` |
| Orbit lines (section 8) | `StylizedOrbitLines` (A-10) | The stylized variant of `Line` |
| Labels (section 8) | `StylizedLabels` (A-10) | `BodyIcon` in the HUD |
| The spaceship (section 11) | `ShipPilot` (Track B) | `Ship` (with `ShipMesh`), and `ShipShadow` for its contact shade |

The mockups come from `Tests/PlayMode/ArtDirectionMockups.cs`, with the shaders in `Assets/UniverseSim/Mockups/`. They were the reference implementation for A-05 to A-09. The viewer now draws everything but the atmospheres, so the halo mockup stays as the reference for A-07, and the mockups are removed when A-07 is done. To render them again (the Editor must not have the project open):

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
| Sun disk | (1.00, 0.93, 0.80) at 4x | The Sun's disk, above the bloom threshold |
| Corona | (1.00, 0.78, 0.48) | The glow around the Sun |
| Glare | (1.00, 0.90, 0.72) | The Sun's halo and rays |
| Orbit, planet | (0.40, 0.72, 1.00), opacity 0.8 | Planets' orbit lines and label icons |
| Orbit, dwarf planet | (0.78, 0.60, 1.00), opacity 0.8 | Dwarf planets' orbit lines and label icons |
| Orbit, moon | (0.40, 0.92, 0.80), opacity 0.65 | Moons' orbit lines and label icons |
| Orbit, approximate | (1.00, 0.62, 0.28), opacity 0.8 | Lines whose positions are approximate (ADR-0008), dashed |
| Sun icon | (1.00, 0.84, 0.40) | The Sun's label icon |

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
| Rims near the ground | Both rims fade out as the camera comes down from 5 % to 0.5 % of the radius above the drawn surface (smoothstep): they outline a body seen from afar, and near the ground every view is grazing, so they would light the whole ground (added 2026-11-02 with B-06). |
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

## 5. Atmospheres and other physical cues (A-07, B-06)

Implemented by `StylizedAtmospheres` (`AtmosphereHalo.shader`) and `StylizedSky` (`Sky.shader`), [ADR-0018](adr/0018-atmospheres-and-sky-cues.md).

**Which bodies.** A halo and a sky appear only on bodies the core reports with an atmosphere (`usim_body_atmosphere`): a positive surface pressure or a reference pressure level in the pack's surface conditions. In the default pack: Venus, the Earth, Mars, Jupiter, Saturn, Uranus, Neptune and Pluto. Mercury and the Moon (0 bar in the source's table) get neither. A body whose conditions are not in the pack (Titan, for now) gets neither until a source is added.

**The halo.**

| Part | Rule |
|---|---|
| Shell | 12 % of the radius on the Earth; the other bodies scale it by their thickness below. |
| Profile | Computed from the view ray. The glow peaks at the limb, falls off over the shell with exponent 2.2 at intensity 2.1 times the body's intensity, and reaches onto the disc as b^16, where b is the ray's closest approach in body radii. |
| Color | From the limb color to the outer color across the shell. Where the limb meets the terminator, 60 % toward the body's sourced twilight hue; no tint without one. |
| Sunlit side | Brightest toward the light: smoothstep(-0.25, 0.45) of the limb direction against the core's light direction. |
| From inside | Below the top of the shell the halo draws its back faces and fades out toward the ground (from the top to a fifth of the shell's height), handing over to the sky. |

Per body (display choices, colors as Unity sRGB):

| Body | Outer color | Limb color | Thickness | Intensity | Note |
|---|---|---|---|---|---|
| Venus | (1.00, 0.88, 0.62) | (1.00, 0.95, 0.82) | 0.6 | 0.6 | |
| Earth | (0.20, 0.45, 1.00) | (0.55, 0.80, 1.00) | 1.0 | 1.0 | A-01 mockups |
| Mars | (0.85, 0.80, 0.80) | (0.95, 0.90, 0.85) | 0.5 | 0.35 | A-01 mockups |
| Jupiter | (1.00, 0.85, 0.65) | (1.00, 0.95, 0.85) | 0.5 | 0.55 | A-01 mockups |
| Saturn | (1.00, 0.88, 0.62) | (1.00, 0.95, 0.85) | 0.5 | 0.45 | A-01 mockups |
| Uranus | (0.55, 0.85, 0.95) | (0.80, 0.95, 1.00) | 0.5 | 0.45 | |
| Neptune | (0.30, 0.50, 1.00) | (0.60, 0.75, 1.00) | 0.5 | 0.5 | |
| Pluto | (0.35, 0.55, 1.00) | (0.60, 0.75, 1.00) | 0.35 | 0.3 | Blue after New Horizons' haze (NASA Photojournal PIA19964: "Pluto's haze layer shows its blue color") |
| Any other | (0.80, 0.85, 0.95) | (0.92, 0.94, 1.00) | 0.4 | 0.4 | |

**The sky at a surface site.** Near the ground of a body with an atmosphere the sky is drawn over the stars and under the bodies:

| Part | Rule |
|---|---|
| Height | Full from the ground to a fifth of the halo's height, gone at the top of the halo (the halo's handover in reverse). On the Earth that is 12 % of the radius, far higher than a real sky: a registered exaggeration. |
| Strength | Times log10(p / 1 Pa) / 3, clamped to 0 to 1, with p the surface pressure (the reference pressure for a giant): full from 1,000 Pa (Venus, the Earth, Mars and the giants), 0.33 at 10 Pa, 0 for Pluto (about 1 Pa). A display mapping that never reverses the order of the pressures. Full from 1,000 Pa so that the skies with a sourced daytime hue (Venus, the Earth, Mars) are all opaque by day; with 5 decades Mars's let the stars show through. |
| By day | The daytime sky hue, brighter toward the horizon (+20 %), at opacity 0.97 times the strength; day comes in over smoothstep(-0.12, 0.2) of the sine of the Sun's elevation, from the core's light direction. |
| Twilight | With the Sun near the horizon (within about 20 degrees), toward the twilight hue around the Sun (the sky's direction to the Sun to the 6th power, at least 30 % everywhere), only for a body with a sourced twilight hue. |
| Night | Clear: the stars show. |
| No sourced daytime hue | A neutral gray (0.72, 0.74, 0.78), and the ship panel says the color is not in the data. |

**The hues.** The cues in `data/catalog/sky-cues.json` name a hue from a fixed vocabulary, each drawn in one color (sRGB). The hues are facts from their sources; these colors are display choices:

| Hue | Color | Used for (default pack) |
|---|---|---|
| `blue` | (0.42, 0.66, 1.00) | The Earth's daytime sky (Tier 2), Mars's twilight |
| `red_and_yellow` | (1.00, 0.52, 0.25) | The Earth's twilight |
| `brownish_gray` | (0.74, 0.62, 0.52) | Mars's daytime sky |
| `orange` | (1.00, 0.66, 0.32) | Venus's daylight at the surface |

**Oceans.** The sun glint on oceans of the chosen look waits for a registered source of where the oceans are (a land and water mask): plan task A-16.

## 6. Stars and rings (A-09)

| Part | Rule |
|---|---|
| Stars | The ADR-0011 mapping of V and B-V, then exposure x1.9 and size x1.5. Stars with a display intensity above 0.9 get a four-point sparkle up to 5 star radii long, at strength 0.8, growing with intensity, so a brighter star never looks fainter. |
| Rings | The same radii, profile, slant opacity, lit and unlit sides and planet shadow. Color goes from Rings, sparse to Rings, dense as the optical depth grows to 2, opacity gain x1.5 and brightness x1.2. Both mappings rise with optical depth. |

`StarAndRingOrderTests` draws synthetic stars (a ladder of magnitudes and of color indices) and a synthetic ring profile (eleven bands of rising optical depth, face on and at 60 degrees) through the viewer's shaders and display mapping, in both styles, before post-processing. It checks that each star's light and peak rise with its brightness, that the ratio of red to blue light rises with B-V, and that each ring band's light and opacity rise with its optical depth.

## 7. Camera (A-08)

| Part | Stylized | Realistic |
|---|---|---|
| Bloom | Intensity 0.9, threshold 1.33 (gamma, about 1.9 linear), scatter 0.7, high-quality filtering | Off |
| Color adjustments | Saturation +15, contrast +8 | Off |
| Vignette | 0.3, smoothness 0.5 | Off |
| Tonemapping | Neutral | Neutral |
| Background | Sky (section 2) | Black |
| The Sun's disk | Sun disk color (section 2) at 4x, so it blooms; its true size | White, its true size |
| Corona | Corona color, 1.6 at the limb, falling off as the distance in solar radii to the power -2.2 and fading out at 4 solar radii | None |
| Glare | Glare color: a halo of 0.9 at the limb and six rays of 0.8 along fixed screen directions (sharpness 48), falling off from the limb over lengths that grow with the disk's radius on screen: the halo over half the radius (4 to 16 px), the rays over three radii (12 to 90 px) | None |

URP's bloom has a fixed soft knee of half the threshold. The threshold of about 1.9 linear therefore keeps everything below 0.95 out of the bloom (the brightness cap of section 3), so only atmospheres, stars and the Sun bloom. The scene's own volume is the URP template's (bloom 0.25, vignette 0.2, Neutral tonemapping) with its bloom and vignette turned off, so the realistic camera only tonemaps.

**The Sun.** The disk is the snapshot's sphere in every scale mode; only its color changes. The corona and glare are a quad at the Sun's center, depth-tested there, so the disk's own front surface hides them: nothing is drawn over the disk, and a body in front of the Sun hides the glow behind it. The corona is measured in solar radii, so it grows and shrinks with the true disk. The halo and rays are measured in pixels from the limb, with lengths between a floor and a ceiling: a distant Sun sparkles like a bright star without hiding the inner planets, and a near one glares. Everything fades out before the edge of the quad, so its outline never shows. The halo and rays are scaled by the part of the disk that no drawn body covers (nine points on the disk tested against the bodies' bounding spheres), so a covered Sun keeps only its corona around the covering body's limb, as in a total eclipse.

**Checks.** `StylizedSunTests` measures the disk's radius along the row through its center, before post-processing, in both styles and both scale modes. It checks the radius against the projected one and against the realistic disk, that the glow is there, and that nothing around the disk reaches its brightness. `BodyShadingTests` draws a white crescent Moon at a phase angle of 150 degrees with the Sun 30 degrees away in the same view, with all post-processing on. It checks that the terminator stays within one pixel on every scan line, that the same view with only the bloom off has the same crescent and night side (the bloom shows around the Sun but changed neither at three decimals), and that the crescent stays more than three times as bright as the night side. The measured ratio is 3.45, against 5.7 in the realistic style; the difference is section 3's lifted night side and night rim, not the bloom. The bloom kept the values chosen in A-01.

## 8. Orbit lines and labels (A-10)

| Part | Stylized | Realistic |
|---|---|---|
| Width | 2.6 px | 1.5 px |
| Color | By kind of body: Orbit, planet; dwarf planet; moon (section 2). Lines whose positions are approximate are Orbit, approximate, and dashed: 8 path points per dash period, 60 % drawn | Planets blue, moons grey-blue, approximate lines orange |
| Fade | Opacity from 1 just behind the body to 0.25 just ahead of it, as 0.25 + 0.75 (1 - f)^1.5 over the fraction f of the path behind the body | None |
| Focus | Opacity x1.35 for the focused body's orbit and the orbits around the focused body, x0.75 for the other lines | None |
| Labels | A 12 px icon before the name, in the line colors: a disc with eight rays for the Sun (Sun icon), a disc for a planet, a ring for a dwarf planet, a crescent for a moon | The name only |

**Lines follow the data.** Only width, color and opacity change; every vertex is the core's path point, the same in both styles. The core samples each path in time order, from half a turn behind the body to half a turn ahead, so the fade runs back in time from the body and shows which way it moves. The body's place on the path is the path point nearest to where it is drawn. The icons are drawn by code (`BodyIcon`), so they are no artistic asset, and their shapes say only what kind of body it is, never what it looks like.

**Checks.** `OrbitLineStyleTests`: the drawn vertices are identical in both styles and are the core's path points; three days earlier the Earth sits at an earlier point of the same path, just behind where the fade starts; the focused body's lines are the highlighted ones; approximate lines are in their own category exactly when the core flags them, in a hue more than 60 degrees from every other and dashed; and the icons match the kind of body and its line color and show in the stylized style only.

## 9. Effects in priority order

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

As of 2026-11-02, items 1 to 6 and 8 are done and item 9 is implemented (section 12), waiting for the owner's hands-on check: items 5, 6 and 8, which need no data, went before the atmospheres (4), which came with ADR-0018 and the sky at a surface site (B-06). The glint (7) waits for a source of where the oceans are (A-16).

## 10. Checks

| Check | Where |
|---|---|
| The terminator is within a pixel of where the light puts it, in both styles | `BodyShadingTests` (A-05) |
| Each map's average hue is within 5 degrees, and grey maps stay grey | `SurfaceMapTreatmentTests` (A-06) |
| No surface exceeds 0.95, so no airless body blooms | `BodyShadingTests` (A-05) |
| The Sun's disk keeps its true size in each scale mode, and its glow stays below it | `StylizedSunTests` (A-08) |
| A crescent next to the Sun keeps its terminator and its contrast under the bloom | `BodyShadingTests` (A-08) |
| Star brightness and color order and ring density order are kept | `StarAndRingOrderTests` (A-09) |
| The ship's paint stays below the cap from every side and under every light, so only its engines bloom; in a body's shadow its outline stays readable | `ShipLookTests` (B-05) |
| A landed ship's contact shade lies on the ground straight below it and darkens the ground beside it | `ShipTests` (B-05) |
| Orbit lines follow the core's paths in both styles, fade back in time from the body, and keep approximate orbits distinct | `OrbitLineStyleTests` (A-10) |
| Halos and the surface sky appear only on bodies the core reports with an atmosphere, the twilight tint only with a sourced hue, the halo brightest on the sunlit side and nothing beyond its shell, the sky's sourced hue by day and a clear sky at night | `AtmosphereTests` (A-07, B-06) |
| Every exaggeration above is in the "what is exaggerated" register | `ExaggerationTests` (A-03) |
| Every artistic asset (under `Assets/UniverseSim/Art/`) is listed in `ATTRIBUTION.md` with its license, an artist's impression also with its references | `ArtisticAttributionTests` and the pipeline's `attribution` (A-04) |

## 11. The spaceship (Track B)

A small, friendly craft in the same Adventure look as the bodies ([ADR-0016](adr/0016-spaceship-and-exploration-values.md)), so it sits in the world instead of on top of it. It is generated by code (`ShipMesh`) and drawn by the `Ship` shader. It flies a short way ahead of the camera and always takes about a sixth of the view's width, so its size never stands for a real one.

| Part | Rule |
|---|---|
| Shapes | Chunky, rounded and clay-like, like the bodies: smooth normals everywhere, no flat plates or hard edges. A plump teardrop hull with a blunt coral nose and a tail that tapers between the engines, a glass bubble canopy, thick swept wings and twin canted fins with rounded tips, and capsule engines with coral intakes and a dark bell around a recessed nozzle. |
| Shading | Section 3's rules from the bodies' own parameters (the global properties of `StylizedBodyShading`): the day curve from Shade to Light, the antialiased terminator, the night side tinted Night, the satin sheen, the day and night rims and the brightness cap, so no part of the hull blooms. Two differences for a small craft: the night side is lifted to at least 15 % (bodies 12 %), and the rims do not fade near the ground, since they outline the ship's own silhouette. |
| Light | The core's light direction at the body it is near. In that body's shadow it is all night side, outlined by the night rim. |
| Occlusion | Baked where the parts meet (each part stands in as a few spheres for the others, with Quilez's sphere occlusion), at 70 % strength and never below 40 % open, so joints get a soft shade and never an ink line. |
| Canopy | Glass: a tight white highlight (exponent 90, strength 0.8) in place of the sheen, and rims 1.6 times stronger. |
| Under a sky | Near the ground of a body with an atmosphere (section 5) the sky's daytime hue lights the ship by day, at 45 % of the sky's strength, more on faces turned up, and catches its edges (strength 0.5, exponent 3). It goes with the daylight, as the sky does. |
| On the ground | A landed ship gets a soft, cool contact shade on the ground straight under it: the ground's color multiplied toward (0.30, 0.32, 0.46) at 80 % at the center, falling off as (1 - r^2)^1.5 over an ellipse 1.7 by 1.8 ship lengths, and fading out as the ship rises 1.2 ship lengths. It is occlusion, not a cast shadow: it does not follow the light and moves no terminator. It is drawn after the bodies and before the ship without a depth test, since the drawn sphere's facets lie a little off the ground plane; a quad on the ground below the camera can only fall on the ground and the ship. |
| Engines | The nozzles glow and bloom with thrust. Nothing else on the ship reaches the bloom. |
| Chase view | In flight the view looks down at the ship 8 degrees more than its heading, so it is seen from about 20 degrees above and its top, canopy and wings show. |

| Part | Color (Unity, sRGB) |
|---|---|
| Hull | (0.88, 0.85, 0.80) |
| Accent (nose, wings, fins, engine intakes) | (1.00, 0.50, 0.32) |
| Canopy | (0.20, 0.55, 1.00) |
| Engine trim | (0.30, 0.33, 0.42) |
| Nozzle glow | (0.45, 0.85, 1.00), emitted at up to 4x, so it blooms |

The ship is listed in the "what is exaggerated" panel whenever it is out ("Spaceship", "Flights"), and it does not exist in the realistic style.

On solid ground it lands ([ADR-0017](adr/0017-surface-conditions.md)): it comes to rest with its lowest point on the reference surface, at the size it has with a view of 15 km per render unit (the camera stays about a kilometer up), and the view is lowered onto the drawn sphere so the ship sits on the ground as drawn. Above a giant planet's 1 bar level it stops 2 km up and never lands; where the data does not say what the surface is, it hovers as before. Near the ground of a body with an atmosphere the sky of section 5 surrounds it, and the panel names the atmosphere and cites the sky's hues ([ADR-0018](adr/0018-atmospheres-and-sky-cues.md)).

## 12. Interaction feel (A-12)

Motion and feedback that make the viewer pleasant to handle (FR-REN-15). They are presentation only: nothing here moves a body, changes the clock or alters a value, and they are the same in both visual styles, since they exaggerate nothing.

| Part | Rule |
|---|---|
| Flights between bodies | The camera moves straight toward the new focus with the distance interpolated on a log scale by smootherstep (6s^5 - 15s^4 + 10s^3: a slow start and a soft landing). A flight lasts 0.9 s plus 0.45 s per decade of distance it covers, at most 3 s. The view turns from where it looked to the new focus over the first 45 % of the flight (an eased slerp), instead of at once. |
| Zoom | Wheel and key steps move a target distance; the distance eases to it on a log scale with a time constant of 0.08 s. |
| Hover | The body under the pointer, as a click would pick it, or the body whose label the pointer is over: a thin white ring around it (7 px outside its disk, at least 9 px in radius), its label brightens and grows by 8 %, and the cursor becomes a ring with a dot (`PickCursor`, drawn by code). Hovering selects nothing. |
| Selection | A gold ring (the selected label's color) around the selected body. A new selection starts the ring at 1.8 times its size and clear, and it settles in over 0.35 s (ease-out-back); the info panel slides in 12 px and fades up from 35 % over 0.25 s. A ring that would be larger than the view is not drawn. |
| Labels | A click on a label selects its body and a double-click flies to it, as on the body itself. |
| Buttons and toggles | Background color and a 3 % growth on hover and a 3 % shrink while pressed, over 0.12 s; toggles' boxes grow by 8 % on hover. |

The rings move by translate and are resized only when their size changes by more than half a pixel, so they cost no layout pass per frame.

### 12.1 Play (ADR-0019)

| Part | Rule |
|---|---|
| Start | Near the Earth, 8 radii out, across from the Moon, turned 25 degrees toward the Sun and raised 12 degrees, so the day side shows with the Moon beside it; in the ship in the stylized style. A "Welcome aboard" toast says how to fly. |
| Mouse steering | The pointer's offset from the view's center turns the flying ship: nothing within 12 % of half the view's shorter side, the full 60 degrees per second at 85 %, eased (squared) between. A faint ring marks the dead zone and a dot shows the turn. Never over the HUD, while dragging or typing. |
| Speed streaks | 180 pale blue-white streaks (0.75, 0.85, 1) around the view axis, from 0.22 to 1.5 render units off it, rushing toward the camera and wrapping back; additive, depth-tested, Transparent queue. They start at a pace of 1.2 heights per second and are full at 4.5 (boosting), and follow the sine of a trip's progress; rise with a 0.25 s time constant and settle with 0.6 s. The view widens by up to 9 degrees with them. Stylized style only, listed as exaggerated. |
| Arrival | The body's name (44 px, bold, outlined), "a moon circling the Earth", and one fact on a dark plate, in the upper part of the view for 5 s; fades and scales in over 0.6 s. |
| Toasts | Under the top bar, gold-edged, at most three, 4.5 s each, in from 10 px above over 0.35 s. |
| Logbook | A gold-edged button in the top bar ("Logbook 3/14 (J)") and panel; reached goals in green with a check mark. |
| Sound | Ambient music (a 64 s cycle of four open chords in D with a drone, sparse high bells, a crossed echo), the engine (a 42 to 76 Hz hum with brown-noise rumble, a whine when boosting, a rising noise band during a trip) and interface sounds (a click, a two-note ping on selection, a rising chime for a goal, a soft chord on arrival, a sweep on departure). Synthesized at run time; Sound (M) and Music toggles. |

## 13. Effects tiers (A-13)

The stylized style keeps 60 frames per second at 1920 x 1080 (NFR-11) by lightening its costliest effects before the frame rate drops (`EffectsQuality`). The tiers change how much is drawn, never a value, a position, or which bodies show an effect that implies a property: halos and the surface sky stay at every tier.

| Tier | What changes |
|---|---|
| High | Everything as chosen in A-01. |
| Medium | The bloom from a quarter-size image without its high quality filter; no map smoothing (the grade stays). |
| Low | No bloom, no map smoothing, no star sparkles. |

In automatic mode (the default) the tier starts high, steps down when the smoothed frame time (time constant 0.5 s) stays over 1/60 s for 1.5 s, and steps back up after 8 s under 60 % of it; nothing changes for 2 s after a start or a change, and frames over 1 s (a pause, a stalled window) are ignored. A fixed tier can be chosen instead and is remembered. Below the high tier the style indicator says so ("Stylized style (effects low)"), and the "what is exaggerated" panel leaves out what is off. Batch runs (the test suites) keep the high tier.

Measured on 2026-11-02 in a Windows player build (`PerformanceBenchmark`) on the development machine (Intel Core i7-13700H, NVIDIA GeForce RTX 4060 Laptop GPU, Direct3D 12), at 1920 x 1080 with vertical sync off and the moons pack, mean frame time and 99th percentile:

| View | Realistic | Stylized, high | Stylized, low |
|---|---|---|---|
| The system (start view) | 1.38 ms (2.47) | 1.51 ms (3.05) | 1.44 ms (2.59) |
| Jupiter system | 1.82 ms (3.02) | 1.98 ms (3.54) | 1.87 ms (3.10) |
| Earth, 4 radii | 1.66 ms (2.92) | 1.71 ms (2.51) | 1.76 ms (3.50) |
| Saturn, 3 radii | 2.30 ms (3.68) | 2.36 ms (3.52) | 2.33 ms (3.76) |
| Landed on Mars (ship, sky, halo) | n/a | 1.82 ms (3.26) | 1.75 ms (2.79) |

Every view with every effect on runs above 420 frames per second, seven times the target, and the automatic tier stays high there. The stylized effects add 0.05 to 0.16 ms to the realistic frame (Earth and Jupiter). The tiers exist for slower machines.
