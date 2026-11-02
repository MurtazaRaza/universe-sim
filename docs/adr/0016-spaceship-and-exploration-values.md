# ADR-0016: A spaceship in the stylized style, with exploration values from the core

| | |
|---|---|
| Status | Accepted |
| Date | 2026-11-02 |

## Context

The project owner asked for a spaceship in the stylized (non-realistic) view, to show what traveling between planets would be like and what their surfaces would be like. On 2026-11-02 the owner chose, among the options put to them:

| Question | Choice |
|---|---|
| How travel works | Fast flight with real facts: the ship flies in seconds, and the HUD gives the real distance, the light time and an estimate of what a real mission would take. Real transfer trajectories were not chosen for now. |
| What a surface visit shows | Going down over a point of the real surface map, with a panel of real conditions there and the real sky. Real terrain relief was not chosen (it needs new elevation sources and a terrain renderer). |
| The ship's model | An original model built in the project. |

The rules in force:

- A spaceship is fictional. SPEC section 3.3 allows no fictional bodies outside a labeled sandbox, P7 requires anything hypothetical or exaggerated to be identifiable, and P8 forbids teaching something false ("the test" of SPEC section 11.1).
- Unity does no orbital mechanics, frame math or ephemeris evaluation (CLAUDE.md rule 1), and Unity physics is never used for celestial motion (SPEC section 3.3).
- Physical values come from registered sources through the pipeline (rule 2). The pack has GM, radii, rotation models and ephemerides, but no surface temperature or pressure, no topography, and nothing that says whether a body's reference surface (its IAU radii) is solid ground or, for the giant planets, a cloud-top pressure level.

## Decision

1. **The ship is the viewer's avatar, not a body.** It is not in the catalog, never changes the clock or anything the core computes, and exists only in the stylized style (the realistic style puts it away and cannot take it out). It is a style effect like the others (ADR-0015): it declares "Spaceship: a fictional craft for exploring" and "Flights: compressed in time" in the "what is exaggerated" panel whenever it is out. It is drawn at a fixed size on screen, about a third of a render unit ahead of the camera, so it never meets a body (the nearest surface is about one render unit away, ADR-0009).
2. **Navigation, not physics.** Like the camera rig's free fly, the ship keeps its position in double precision relative to the nearest body (its anchor), in host axes and the ECLIPJ2000 frame. No gravity acts on it. Changing the anchor adds the core's relative state, and every surface coordinate comes from the core.
3. **Fast trips with real facts.** A trip to another body takes 4 to 14 seconds of real time on a log-distance path that eases in and out. When it starts, the HUD shows the core's numbers for the two bodies: their distance, the light time, how the flight compares with the speed of light, and a two-body estimate of a real trip.
4. **The core computes the exploration values** (`usim::orbits`, `usim::surface`, `usim/sim/exploration.hpp`; C ABI version 5; CLI commands `transfer`, `site` and `day`). All are derived from the pack's ephemeris, rotation models, GM and radii, so no new physical input is needed:

   | Value | Definition |
   |---|---|
   | Transfer estimate | A Hohmann transfer between circular coplanar orbits around the first body the two bodies' orbit-center chains share (`default_orbit_center`). The radii are osculating semi-major axes at the instant: around the star at the system barycenter where the catalog has one, with GM the sum of the center's and the system's; the transfer itself uses the center's GM. When the center is one of the two bodies, that end is its surface (mean radius). Flags mark a surface end, a trip between systems (the last leg to a moon is not included) and approximate positions (ADR-0008). Distance and light time are geometric, between the two centers. |
   | Surface site | A point above the reference ellipsoid (triaxial radii, else a sphere) at planetocentric latitude, east longitude and altitude along the radius, the convention of the surface maps (ADR-0014). Its horizon is the ellipsoid normal with north and east; the Sun's elevation, azimuth, distance, apparent diameter, sunlight relative to 1 au and the local solar time (true Sun) are geometric, without light time or refraction. Gravity and escape speed are GM / r^2 and sqrt(2 GM / r) at the reference surface below, without rotation. |
   | Day length | The sidereal rotation period from the mean rotation rate: the secular part of the IAU prime meridian polynomial (W1 + 2 W2 d), without its periodic terms. Only a body without IAU elements uses the instantaneous rate, flagged. The mean solar day is 2 pi / |n_rot - n_orb|, with n_rot signed by the sense of rotation relative to the orbit around the star of the body's system, and n_orb that osculating orbit's mean motion. |

   Validation (`core/tests/validation/test_exploration.cpp`): the mean solar day against the Sun's hour angle followed through the ephemeris and orientation over whole orbits for Mercury, Venus, the Earth, the Moon and Mars. The threshold is 5e-5; the measured differences are 1.1e-5 at most. Transfer estimates and sites are checked against independent computations from the same pack, and the two-body and ellipsoid functions against independent relations on synthetic values.
5. **Hovering, not landing.** "Descend" takes the ship down to hover over the point below it, by default at 1 % of the body's mean radius (at least 2 km), adjustable from 2 km to half a radius. The panel shows the derived values above and lists what is not yet in the data: surface temperature and pressure, and whether the reference surface is solid ground or cloud tops. Until a registered source says which bodies have a solid surface, the ship never shows itself standing on one, so it cannot appear parked on Jupiter's clouds. The sky is drawn without an atmosphere, and the panel says so.
6. **The model is code.** The ship's mesh is generated by `ShipMesh` in the Adventure palette (docs/ART_DIRECTION.md). Like the shaders, it is not an artistic asset file and needs no `ATTRIBUTION.md` entry. A modeled or licensed ship would go through A-04 first.

## Consequences

- SPEC gains UC-10, FR-SIM-11 (exploration values) and FR-REN-16 (the spaceship), and the non-goal on fictional bodies says how the ship relates to it. ARCHITECTURE lists the new modules, the C ABI additions and the Unity components (`ShipPilot`, `ShipHud`, `ShipMesh`, the `Ship` shader, and the camera rig's `ICameraPilot`).
- C ABI version 5 adds `usim_transfer_estimate_at`, `usim_surface_site_at`, `usim_surface_site_below` and `usim_day_length_at` with their structs; the C# binding checks their sizes at startup.
- Follow-up work, in the plan's Track B:
  - Sourced surface conditions. Register a source for which reference surfaces are solid and which are pressure levels (the IAU report, Archinal et al. 2018, gives the giant planets' radii at 1 bar). Add surface temperature and pressure: Tier 1 sources where they exist, else S-NSSDC-FACT as a labeled Tier 2 input (DATA_SOURCES.md section 1). These go through the pipeline into the pack and the C ABI; then the ship can touch down on solid surfaces.
  - Atmospheres in the sky view, with A-07.
  - Terrain relief from elevation models, if the owner wants it later.
  - Real transfer trajectories (a Lambert solver over the Phase 7 propagator), if the owner wants them later.
- The owner checks the feel hands-on (flight speeds, the chase camera, the hover height), as for A-12.

## Alternatives considered

- **Real transfer trajectories now.** A trip that follows a transfer orbit with time acceleration shows launch windows and months of travel, but it needs a Lambert solver and a propagator in the core (Phase 7). The owner chose fast flight with the estimate shown; trajectories remain possible later.
- **Landing with a ground-level view.** Without elevation data the ground is a smooth ellipsoid drawn with a sphere of at most 128 segments, a flat plane at ground level, and a 2048 x 1024 map gives several kilometers per pixel. Standing on Jupiter would also teach something false. Hovering at a modest height shows the map and the curvature honestly.
- **Flight physics in Unity** (a ship pulled by the planets). This would put orbital mechanics in the host and contradict SPEC section 3.3. A ship moved by gravity belongs in the core, with the Phase 7 test-particle integrator.
- **The ship in both styles.** The realistic style exists to show the unembellished data (FR-REN-12), and a fictional craft is an embellishment.
- **A separate overlay camera for the ship.** It is not needed: at a third of a render unit ahead of the camera the ship is always nearer than any surface.
- **The instantaneous rotation rate for the day.** The rotation models include forced librations (Mercury's 88-day term, the Moon's monthly terms), so the instantaneous rate differs from the mean by about 1e-4. Measured against the hour angle over whole orbits, it gave 4.4e-4 for Mercury and 9.6e-5 for the Moon, against 1.2e-6 and 1.1e-5 with the IAU mean rate.
