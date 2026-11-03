# ADR-0019: Play: a ship-first start, facts in human terms, a logbook, time controls and sound

| | |
|---|---|
| Status | Accepted |
| Date | 2026-11-03 |

## Context

At the first hands-on check (B-05, 2026-11-02) the project owner found the viewer not yet fun to use: "Realism is one thing, but fun aspect is also very important" (SPEC P9). The reasons found in the build:

- The first view was the whole system from its edge: the planets were dots, and the info panel led with GM, NAIF IDs and citations.
- The spaceship, the most playful part, was behind a key (V), beside two other control schemes (orbit, free fly).
- Flight had no sense of speed, and nothing marked arriving somewhere.
- There was nothing to aim for, and facts were raw catalog values rather than things a visitor can feel.
- Time was set by typing a date or stepping through rate buttons.

On 2026-11-02 the owner chose, among the options put to them (this closes OQ-10 and OQ-11 and task A-15):

| Question | Choice |
|---|---|
| First pass | All four options: a ship-first start with better flight, facts in human terms, an explorer's logbook, and time as a toy |
| Sound (OQ-11) | Music, engine and interface sounds |
| Later (OQ-10) | Guided tours. Upcoming sky events and a photo mode were not chosen. |

The rules in force are unchanged: the presentation may stylize the look but never the facts (P8, ADR-0015), Unity does no orbital mechanics, time scale conversion or frame math (CLAUDE.md rule 1), and every value shown comes from the core.

## Decision

1. **The viewer starts near the Earth.** At load the camera is placed 8 Earth radii from the Earth's center, on the far side from the Moon, turned 25 degrees toward the Sun's side and raised 12 degrees above the ecliptic, so the Earth's day side fills part of the view with the Moon beside it (`StartExperience`; display choices, computed from the core's positions). The Moon is selected. In the stylized style the spaceship is out at once; in the realistic style the orbit camera starts there. Tests and the frame-rate benchmark keep the system view (`StartExperience.Enabled`, and the `-usim-benchmark` option).
2. **Flight that feels like flying.**
   - The mouse steers the flying ship without a button held: the pointer's offset from the center of the view turns the ship, beyond a dead zone, and never while the pointer is over the HUD. Dragging still steers, and the keys still work.
   - Speed cues (`SpeedCues`, a style effect): pale streaks rush past and the view widens by up to 9 degrees while the ship goes fast (boosting, and mid-trip). Nothing real is out there, so the effect is listed in the "what is exaggerated" panel and exists only in the stylized style.
   - A banner with the body's name, its kind and one fact appears when a trip ends.
   - How fast the ship seems to move is measured from its position as heights per second (`ShipPace`), the rate at which the view changes. It is not a physical speed and is never shown as one.
3. **Facts in human terms first.** The info panel leads with "What it's like here" (`BodyFacts`): size and weight compared with the Earth, the length of the day and the year, the mean temperature, how long sunlight takes to arrive and how strong it is, how long a real spacecraft from the Earth needs, and whether there is air. Every input comes from the core: radii, the surface site's gravity at the equator, the day length, the surface conditions, the transfer estimates (from the Sun for sunlight, from the Earth for the trip) and the atmosphere. As in the ship panel (ADR-0016), the host only takes ratios and converts units. The sourced values, their derivations and the citations stay in the panel, in a "Data and sources" section below.
4. **An explorer's logbook** (`Logbook`, `LogbookHud`). The goals are taking off, flying to and landing on the Moon, an Earthrise, the Sun, landing on Mars, Jupiter's clouds, flying through Saturn's rings, reaching Pluto, visiting every planet, outrunning light, traveling a century in time, watching the Moon go around, and a sunset on another world. A goal whose bodies are not in the pack is left out. The checks compare core values only: distances in body radii, the ship's mode, the latitude of the point below (the ring plane), the Sun's elevation at the ship's site, the clock. Progress is kept in PlayerPrefs, outside the simulation, so determinism (CLAUDE.md rule 9) is unaffected. Reaching a goal shows a toast.
5. **Time as a toy.**
   - A throttle slider covers the rate presets, reverse on the left and forward on the right (`TimeThrottle`). Pause stays a button and Space.
   - A timeline slider spans the pack's coverage. Its position and the jumps it makes go through the core's instant functions (`usim_instant_seconds_between`, `usim_instant_make`): arithmetic on TDB instants, not a conversion between scales.
   - Holding Tab rushes time at the fastest preset in the current direction, and releasing it restores the rate.
   - The date field stays behind a "Set date" button, for jump-to-date (SPEC section 3.1).
6. **Sound synthesized by code** (`SoundScape`, `AmbientMusic`, `EngineSound`, `UiSounds`). The parts are generative ambient music (a slow cycle of open chords, sparse bells, an echo; seeded, so it sounds the same each run), an engine hum and rumble that follow the ship's pace, with a whine when boosting and a sweep during a trip, and short interface sounds for clicks, selections, goals, departures and arrivals. There are no audio files, so like `ShipMesh` nothing needs a license or an `ATTRIBUTION.md` entry; an audio file added later would go through A-04 as an artistic asset. Sound (M) and Music toggles are remembered in PlayerPrefs. The synthesis runs on the audio thread without allocation and reads nothing from the core.
7. **Guided tours later** (task A-22). Upcoming sky events (the core's event finder, validated against CSPICE) and a photo mode were not chosen.

## Consequences

- SPEC: OQ-10 and OQ-11 are decided; FR-REN-17 (logbook), FR-REN-18 (sound) and FR-REN-19 (time controls) are added, and FR-REN-16 gains the start, the mouse steering and the speed cues.
- The plan checks A-15 and adds A-17 to A-23. The owner's hands-on check of all of it joins B-05.
- New Unity components and assets: `StartExperience`, `ShipPace`, `SpeedCues` with `SpeedStreaks.shader`, `BodyFacts`, `TimeThrottle`, `Logbook`, `LogbookHud`, the synthesizers and `SoundScape`; three input actions (RushTime on Tab, ToggleLogbook on J, ToggleMute on M). The core gains nothing; its existing instant, site, day and transfer functions suffice.
- The HUD's time row changes: the Slower, Reverse and Faster buttons give way to the throttle (the [ and ] keys still step it).

## Alternatives considered

- **Locked-cursor mouse look for the ship.** More immersive, but it hides the pointer that picks bodies and reaches the HUD, so every click would first need a key to free the cursor. The pointer-offset steering keeps both.
- **Audio files from a CC0 library.** Richer sound, but every file needs a pinned source and a license entry, and the owner did not supply any. Synthesis needs neither. Files can replace or join it later through A-04.
- **Facts computed in the core.** The comparisons are ratios of core values and unit conversions, which the ship panel already does in the host. A core function would add ABI surface without new physics.
