# Universe Simulation: Project Specification

| | |
|---|---|
| Status | Draft v0.3 |
| Last updated | 2026-10-20 |
| Related | [ARCHITECTURE.md](ARCHITECTURE.md), [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md), [DATA_SOURCES.md](DATA_SOURCES.md), [adr/](adr/) |

This document defines **what** the project is and the requirements it must meet. How the system is structured is in [ARCHITECTURE.md](ARCHITECTURE.md). The order of work is in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md). Decisions and their rationale are recorded as ADRs in [adr/](adr/).

---

## 1. Purpose

Build a physically accurate, data-driven simulation of the universe. It starts with the Solar System and expands outward to the stellar neighborhood, the galaxy and beyond.

Three properties define the project:

1. **Real data.** Every simulated quantity comes from a verified scientific source and can be traced back to it.
2. **Engine-agnostic simulation.** All calculations live in a standalone module that has no knowledge of any rendering engine.
3. **Unity as a renderer only.** Unity displays the state that the simulation produces. It can be replaced or complemented by another engine without touching the simulation.

## 2. Guiding principles

| ID | Principle | Meaning in practice |
|---|---|---|
| P1 | Real data only | No invented, estimated-from-memory or "looks right" values. If a value is not available from a verified source, it is missing, and the system says so. |
| P2 | Engine independence | The simulation core compiles and runs with no engine present. Renderers consume its output through a documented host contract. |
| P3 | Traceability | Every physical value carries a reference to its source (and to its derivation, if it was computed from other values). |
| P4 | Measured accuracy | Accuracy is checked by automated validation against independent references, not assumed. |
| P5 | Determinism and reproducibility | The same data inputs produce the same data pack. The same pack, time and query produce the same result. |
| P6 | Scale-aware from day one | Types, identifiers and frames are designed so that moving beyond the Solar System does not require a rewrite. |
| P7 | Honest presentation | Anything approximate, hypothetical or display-only (for example enlarged planet sizes) is clearly marked in the UI. |

## 3. Scope

### 3.1 Version 1: Solar System MVP

- Sun, the eight planets, the Moon, and Pluto, plus the system barycenters, positioned from the JPL DE440 planetary ephemeris (see [ADR-0004](adr/0004-ephemeris-baseline-de440.md)).
- Default time coverage of 1849-12-26 to 2150-01-22 (the span of `de440s.bsp`). The full DE440 span (1549-12-31 to 2650-01-25) is optional.
- Physical properties (radii, GM, rotation models) from IAU and JPL sources.
- Body orientation from the IAU WGCCRE 2015 rotation models.
- A simulation clock with pause, rate control (forward and reverse) and jump-to-date.
- A Unity viewer with camera navigation across scales, time controls, orbit paths, labels, and a body info panel that shows values with their sources.
- A headless command-line host that queries the core with no renderer, which proves engine independence.

### 3.2 Roadmap after v1

In rough order (details in the [implementation plan](IMPLEMENTATION_PLAN.md)):

1. Major moons of all planets from JPL satellite ephemerides, plus higher-fidelity orientation.
2. Dwarf planets, asteroids and comets from the JPL Small-Body Database.
3. An N-body sandbox for hypothetical scenarios, clearly labeled as such.
4. Beyond the Solar System: nearby stars (Gaia, Hipparcos), exoplanet systems (NASA Exoplanet Archive), then galactic and extragalactic structure.

### 3.3 Non-goals

- **Not a game physics simulation.** Unity physics (PhysX, rigidbodies) is never used for orbital motion.
- **Not a replacement for SPICE in mission-critical work.** Accuracy is high and validated, but the project is for exploration, education and research-grade visualization.
- **No fictional bodies in the baseline.** Hypothetical changes exist only in the sandbox mode and are always labeled.
- **No apparent positions in v1.** The core returns geometric states. Light-time and aberration corrections for observer views are a later addition (see [Open questions](#13-open-questions)).

## 4. Users and use cases

**Users:** an explorer who wants to see the Solar System as it really is; a learner who wants to inspect real data with its sources; a developer or AI agent who extends the simulation with new data, models or renderers.

| ID | Use case |
|---|---|
| UC-01 | Open the viewer and see the Solar System at the current date and time. |
| UC-02 | Jump to any date within data coverage and see correct positions and orientations. |
| UC-03 | Pause, play, reverse, and change the rate of time. |
| UC-04 | Focus on and follow a body. Zoom smoothly from a planet's surface to a whole-system view. |
| UC-05 | Inspect a body: physical and orbital data, with units and source citations. |
| UC-06 | Toggle orbit paths, labels and scale modes (true scale or enlarged bodies). |
| UC-07 | Without a renderer, query the state of any body at any time (CLI or scripting). |
| UC-08 | (Later) Run a sandbox scenario, such as changing a mass or adding a body, and watch the system evolve. |

## 5. Functional requirements

### 5.1 Simulation core

| ID | Requirement |
|---|---|
| FR-SIM-01 | Return the position and velocity of any catalog body at any instant within data coverage, relative to a requested center body and expressed in a requested reference frame. |
| FR-SIM-02 | Return the orientation (body-fixed frame rotation) of bodies that have a rotation model. |
| FR-SIM-03 | Report data coverage (time span) per body. A query outside coverage must return an explicit result such as "out of coverage", or use a fallback model that is flagged as approximate. It must never silently extrapolate. |
| FR-SIM-04 | Every returned state carries quality metadata: which model produced it (ephemeris, analytic or integrated) and which source backs it. |
| FR-SIM-05 | Provide a simulation clock (current instant, signed rate, pause, set time, clamped to coverage). The clock lives in the core, not in the renderer. |
| FR-SIM-06 | Sample orbit paths: produce a polyline of a body's trajectory over an interval, relative to a center body, with adaptive sampling. |
| FR-SIM-07 | Provide a body catalog with identifiers, names, classification and hierarchy (barycenters, primaries, satellites). |
| FR-SIM-08 | Convert between the TDB, TT, TAI and UTC time scales, and format dates in the Gregorian and Julian calendars. |
| FR-SIM-09 | Transform between ICRF, ECLIPJ2000 and body-fixed frames. |
| FR-SIM-10 | Provide engine-agnostic presentation math: re-center positions on a double-precision view origin, apply scale modes, and map axes to a host's coordinate convention. |

### 5.2 Data

| ID | Requirement |
|---|---|
| FR-DATA-01 | All physical values used at runtime come from data packs built by the data pipeline from sources in the registry. |
| FR-DATA-02 | Maintain a source registry recording the provider, URL, citation, version, retrieval date, file checksums and terms of use. |
| FR-DATA-03 | The pipeline is reproducible: the same lock file produces a byte-identical data pack. |
| FR-DATA-04 | Provenance is kept per field: each catalog value references its source and, if derived, how it was derived. |
| FR-DATA-05 | Missing values stay missing. A renderer may use a display fallback (for example a default color), but it must be flagged as non-physical. |
| FR-DATA-06 | Data packs are versioned. The core checks format compatibility when it loads a pack and reports clear errors. |
| FR-DATA-07 | Downloaded files are verified against publisher checksums where they exist (for example the NAIF `aa_checksums.txt` MD5 list) and always against the recorded SHA-256. |

### 5.3 Rendering layer (Unity)

| ID | Requirement |
|---|---|
| FR-REN-01 | Render bodies at core-provided positions with no visible precision artifacts (jitter, z-fighting) at any zoom level, from near a surface to the whole system. |
| FR-REN-02 | Scale modes: true scale and enlarged bodies (a radius exaggeration factor). The active mode is always visible in the UI. |
| FR-REN-03 | Camera: focus on and orbit a body, free-fly, and make smooth transitions across scales. |
| FR-REN-04 | Time controls: play and pause, signed rate, jump to date, jump to now. |
| FR-REN-05 | Body info panel with values, units and source citations. |
| FR-REN-06 | Orbit lines and labels, each with a toggle. |
| FR-REN-07 | Lighting that comes from the Sun's actual direction at every scale (planet and moon phases must be correct). |
| FR-REN-08 | The renderer never computes or modifies physical state. It reads snapshots from the core and sends user commands (time changes, focus) back to it. |

### 5.4 Headless host

| ID | Requirement |
|---|---|
| FR-CLI-01 | A console host loads a data pack and answers queries (state, catalog, snapshot, orbit path) as JSON or CSV. It runs in tests and scripts with no engine installed. |

## 6. Non-functional requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-01 | Portability | The core is written in C++20 using only the standard library: zero third-party runtime dependencies and zero engine references. Hosts in other languages use its stable C ABI ([ADR-0002](adr/0002-core-language-and-unity-integration.md)). |
| NFR-02 | Precision | The core uses double precision throughout. Single precision is used only inside a host, after positions are re-centered on the view origin. |
| NFR-03 | Performance | A full snapshot of all v1 bodies takes under 0.1 ms on a current desktop CPU (target, to be measured in Phase 3). Steady-state per-frame calls make no heap allocations in the core, and the host bindings make no managed allocations (to avoid GC spikes in Unity). |
| NFR-04 | Determinism | The same inputs give identical outputs on the same platform and compiler. Fast-math and floating-point contraction (FMA fusion) are disabled. |
| NFR-05 | Testability | Core tests run with `ctest` and do not require Unity. |
| NFR-06 | Accuracy | Validation thresholds are defined in [section 9](#9-accuracy-and-validation) and enforced by automated tests. |
| NFR-07 | Data size | The default development data pack is at most 50 MB. Larger optional packs are allowed. |
| NFR-08 | Platforms | Desktop first: macOS (Apple Silicon) and Windows. Other targets are an open question. |
| NFR-09 | Documentation | Every public core API member has a `///` doc comment that states units, frame and time scale wherever they apply. The C ABI header and the C# bindings document their members the same way. |
| NFR-10 | Licensing | Every redistributed dataset and asset has its license or terms recorded in the source registry or an attribution file. |

## 7. Domain conventions

These conventions are binding for all code. The rationale is in [ADR-0003](adr/0003-units-time-and-reference-frames.md).

### 7.1 Units

| Quantity | Internal unit |
|---|---|
| Length, position | kilometer (km) |
| Time, duration | second (s) of TDB |
| Velocity | km/s |
| Angle | radian |
| Gravitational parameter | GM in km^3/s^2 |
| Mass | Not stored as a primary value. Where shown, mass is derived as GM / G and flagged as derived, because GM is known far more precisely than G. |

Display units (au, light-years, degrees, calendar dates) are applied only in presentation code.

### 7.2 Time

- The internal time scale is **TDB** (Barycentric Dynamical Time), the independent variable of the JPL ephemerides.
- An `Instant` is stored in two parts (whole days since J2000.0 plus seconds of day) to keep sub-microsecond resolution across millennia. TT and TAI readings use the same representation as distinct types; UTC readings are calendar fields, because UTC days are not uniform.
- TDB - TT follows the model SPICE uses (DELTET; Moyer 1981), with its parameters read from the leap seconds kernel. The kernel states it is accurate to about 30 microseconds, and it matches the validation oracle exactly.
- J2000.0 = JD 2451545.0 TDB = 2000-01-01T12:00:00 TDB.
- UTC conversions use the leap second table from the registered source (NAIF LSK, IERS Bulletin C), including 23:59:60 during a leap second. UTC with leap seconds is defined only from 1972-01-01; earlier UTC requests fail with an explicit error. For earlier dates the UI shows TDB or TT; a ΔT model for historical UT is a later task (OQ-04). After the last table entry no further leap seconds are assumed.
- Calendar display matches JPL Horizons by default: Gregorian on and after 1582-10-15, Julian before that, with options for proleptic Gregorian and proleptic Julian. In the default calendar the dates 1582-10-05 to 1582-10-14 do not exist and are rejected. Years use astronomical numbering (year 0 is 1 B.C.) and ISO 8601 text carries an explicit scale suffix, for example `2000-01-01T12:00:00 TDB`.

### 7.3 Reference frames

- **ICRF** (labeled `J2000` in SPICE) is the primary inertial frame. JPL DE kernels are expressed in it.
- **ECLIPJ2000** (mean ecliptic and equinox of J2000, with the obliquity value used by SPICE) is the default frame for rendering and display.
- **Body-fixed frames** are defined by IAU rotation models.
- The default center is the **Solar System Barycenter** (NAIF ID 0).
- The core is **right-handed**. Mapping to a host's convention (for example Unity's left-handed, Y-up system) is done only by the presentation axis mapping, never ad hoc in host code.

### 7.4 Identifiers

- A body identifier is a pair (`namespace`, `value`).
- Solar System bodies use the `naif` namespace with NAIF integer IDs (0 = SSB, 10 = Sun, 399 = Earth, 301 = Moon, 3 = Earth-Moon barycenter, 2000001 = Ceres, ...).
- Later namespaces include `gaia-dr3` (Gaia source_id) and `hip` (Hipparcos). Cross-identifications are stored as data.

### 7.5 Classification and hierarchy

- Kinds: `Barycenter`, `Star`, `Planet`, `DwarfPlanet`, `Moon`, `Asteroid`, `Comet`, and later `Spacecraft`, `StarSystem`, `Galaxy`.
- The hierarchy is a tree that mirrors the ephemeris structure: SSB > Sun and planet-system barycenters > planets and their moons. Each body has a primary (the body it orbits) used for orbit display and relative queries.

## 8. Simulation models

| Model | Used for | Data | Accuracy | Phase |
|---|---|---|---|---|
| **Ephemeris** (Chebyshev evaluation of JPL SPK segments) | Sun, planets, Moon, Pluto; later moons and selected small bodies | DE440, JPL satellite SPKs, Horizons-generated SPKs | Matches the source kernel to numerical precision | 3, 6, 7 |
| **Rotation** (IAU WGCCRE models) | Orientation of all bodies with a model | `pck00011.tpc` | As stated by the IAU report per body | 4, 6 |
| **Two-body Keplerian** | Small bodies without an SPK; fallback | SBDB osculating elements at epoch | Error grows with distance from the elements' epoch; always flagged approximate | 7 |
| **Test-particle integration** | Small bodies and (later) spacecraft moving in the gravity field of ephemeris bodies | SBDB elements, DE440 states, GM values | Validated against Horizons | 7 |
| **N-body sandbox** | Hypothetical scenarios | Initial states from DE440 at a chosen epoch, GM from DE440 | Drift against DE440 measured and reported | 8 |

**Model precedence:** ephemeris > integrated > analytic. The state metadata always names the model that produced the value (FR-SIM-04).

## 9. Accuracy and validation

Validation tests compare the core against independent references. Reference data is produced by documented, version-pinned scripts and committed under `data/reference/` with its provenance.

| ID | Check | Reference | Threshold |
|---|---|---|---|
| VAL-01 | SPK evaluation: position and velocity of every v1 body, sampled across coverage | CSPICE (through SpiceyPy) reading the same kernel | position ≤ 1 m, velocity ≤ 1 mm/s |
| VAL-02 | End-to-end planetary states, 1900 to 2100 | JPL Horizons API (which uses DE441 for planets) | ≤ 1 km, provisional (*) |
| VAL-03 | Time scale conversions (UTC, TAI, TT, TDB) | SPICE `str2et`/`unitim`, or SOFA | ≤ 0.1 ms, provisional (**) |
| VAL-04 | Body orientation matrices | CSPICE `pxform` with the same PCK | ≤ 1 µrad |
| VAL-05 | Keplerian small-body positions | Horizons small-body ephemerides | Measured and reported; no hard threshold (approximate model) |
| VAL-06 | N-body sandbox drift over 1, 10 and 100 years | DE440 | Measured and reported; thresholds set in Phase 8 |
| VAL-07 | Energy and angular momentum conservation in the sandbox | Analytic invariants | Set in Phase 8 |

(*) Horizons uses DE441 while the core uses DE440. The two ephemerides come from the same fit and are expected to agree closely in the modern era. Phase 3 measures the actual differences and sets a final threshold with the measurement recorded.

(**) SPICE and SOFA use different TDB-TT models, so small sub-millisecond differences are expected. Timing errors matter only when a user-entered UTC time is mapped to TDB. At Earth's orbital speed of about 30 km/s, 0.1 ms corresponds to about 3 m, well below the VAL-02 threshold. The final threshold is set once measured.

Rules:

- Never loosen a validation threshold without recording the measured cause in the plan or in an ADR.
- Validation tests run locally before a numerical change is declared done, and in CI once CI exists (P0-11, deferred).

## 10. Data provenance

Details of the registry, tiers and pipeline are in [DATA_SOURCES.md](DATA_SOURCES.md) and [ADR-0005](adr/0005-data-provenance-and-storage.md). In summary:

- **Only Tier 1 sources** (the producing institution or peer-reviewed publications) may supply simulation inputs. Tier 2 sources (compilations such as the NASA fact sheets) are for cross-checks only. Unsourced material such as wikis, blogs or AI output is never used.
- **No literal physical values in code**, except definitional constants (for example the au, the speed of light, the J2000 epoch, TT - TAI = 32.184 s). These live in one place in the core (`usim/constants.hpp`), each with a citation.
- **Derived values** are computed by the pipeline, which records the formula and the inputs.
- **Manual transcription** (for sources with no machine-readable form) is allowed only in reviewed files under `data/catalog/manual/`. Each entry records the source URL, the access date and the exact table or section.

## 11. Rendering layer (Unity) requirements

- Unity version **6000.5.1f1** with **URP 17.5**, the **Input System** package and **UI Toolkit** for UI.
- The Unity project lives in `UniverseSim/` and consumes the core as a native plugin through its C ABI, using C# P/Invoke bindings ([ADR-0002](adr/0002-core-language-and-unity-integration.md)).
- Unity-side code is limited to: hosting the core, mapping snapshots to GameObjects, camera and input, UI, materials, lighting and visual effects.
- Precision handling: positions arrive re-centered on a double-precision view origin, so the camera stays near the Unity world origin. See [ARCHITECTURE.md](ARCHITECTURE.md#5-host-contract).
- URP point lights cannot cover Solar System distances, so Sun lighting is computed per body from the Sun's direction (for example a custom lit shader or a per-body light setup).
- Textures and other visual assets come from verified sources (NASA, USGS and similar) and are listed in an attribution file in the Unity project.

## 12. Expansion beyond the Solar System

These items are not scheduled in detail, but the design must not block them:

- **Hierarchical frames and domains.** Each star system gets a local frame anchored at its barycenter. Galactic and extragalactic frames sit above them. Each domain may use a different model and level of detail.
- **Stellar neighborhood.** Gaia DR3 (for example the Gaia Catalogue of Nearby Stars) and the Hipparcos new reduction provide positions, parallaxes, proper motions and radial velocities. Linear space motion is a valid model over centuries. Low-significance parallaxes must be handled honestly (no naive 1/parallax for poor measurements).
- **Exoplanets.** The NASA Exoplanet Archive provides orbital and physical parameters with uncertainties. Missing parameters stay missing.
- **Galactic and extragalactic scale.** A research spike will be needed (galaxy structure models, Local Group, large-scale surveys).
- **Identifier namespaces, origin-relative precision and level-of-detail** are already part of the v1 design for this reason.

## 13. Open questions

| ID | Question | Status |
|---|---|---|
| OQ-01 | Baseline ephemeris: DE440 (chosen) or the newer DE442? | Decided in [ADR-0004](adr/0004-ephemeris-baseline-de440.md), revisit after Phase 3 |
| OQ-02 | Default data span and pack size. How should large data be distributed (download on first run or Git LFS)? | Open, Phase 2 |
| OQ-03 | Target platforms beyond desktop (web, mobile, VR)? | Open |
| OQ-04 | Which ΔT model to use for historical dates before 1972 (UT display)? | Open, pick from a verified source |
| OQ-05 | License for the project itself? | Open |
| OQ-06 | Should observer views show apparent (light-time and aberration corrected) positions? | Open, after v1 |
| OQ-07 | Should the Unity Editor hot-reload the native plugin (load a uniquely named copy) so the Editor does not need a restart after each core rebuild? | Open, decide before Phase 5 |

## 14. Glossary

| Term | Meaning |
|---|---|
| Barycenter | Center of mass of a system of bodies, for example the Earth-Moon barycenter (NAIF 3). |
| C ABI | The plain-C function interface (`usim.h`) that the core exposes, so hosts in any language can call it. |
| Chebyshev polynomial | The basis functions JPL ephemerides use to store positions over fixed time intervals. |
| DAF | Double precision Array File, NAIF's binary container format for SPK and binary PCK files. |
| Data pack | The versioned binary file built by the pipeline and loaded by the core. It holds ephemeris segments, catalog values and provenance. |
| ECLIPJ2000 | The mean ecliptic and equinox of J2000 reference frame. |
| Ephemeris | A table or model giving positions of bodies over time. |
| GM | Gravitational parameter (G times mass), known much more precisely than mass alone. |
| ICRF | International Celestial Reference Frame, the fundamental inertial frame. |
| LSK, PCK, SPK | NAIF kernel types: leap seconds, planetary constants (radii, rotation), and position (ephemeris) data. |
| NAIF ID | The integer code identifying a body in SPICE and JPL systems. |
| Osculating elements | Keplerian orbital elements that describe an orbit exactly at one instant (their epoch). |
| SSB | Solar System Barycenter, NAIF ID 0. |
| TAI, TT, TDB, UTC | International Atomic Time; Terrestrial Time (TAI + 32.184 s); Barycentric Dynamical Time; Coordinated Universal Time (TAI minus leap seconds). |
| View origin | The double-precision point that render coordinates are re-centered on before conversion to single precision. |
