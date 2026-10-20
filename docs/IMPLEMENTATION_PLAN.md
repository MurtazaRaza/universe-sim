# Implementation Plan

| | |
|---|---|
| Status | Active |
| Last updated | 2026-10-20 |
| Current phase | Phase 0: Repository foundation |
| Related | [SPEC.md](SPEC.md), [ARCHITECTURE.md](ARCHITECTURE.md), [DATA_SOURCES.md](DATA_SOURCES.md), [adr/](adr/) |

## How to use this plan

- Work proceeds phase by phase. A phase is done when all its tasks are checked **and** its acceptance criteria are met.
- Tasks have stable IDs (`P<phase>-<nn>`). Reference them in commits and notes. Do not renumber; append new tasks at the end of a phase.
- When you finish a task: check its box and add a dated note under it (one or two lines on what was done and any deviation). Update "Current phase" above when a phase completes.
- When a task requires a decision that changes [SPEC.md](SPEC.md) or [ARCHITECTURE.md](ARCHITECTURE.md), write an ADR and update those documents in the same change.
- Tasks marked **(user)** need the user's input or action before or during the work.
- Requirement IDs (FR-*, NFR-*, VAL-*) refer to [SPEC.md](SPEC.md).

## Phase overview

| Phase | Name | Outcome |
|---|---|---|
| 0 | Repository foundation | Repo, docs, solution skeleton, Unity can reference the core |
| 1 | Core foundations | Math, time, frames, catalog model, axis conventions |
| 2 | Data pipeline and data pack | Reproducible fetch and build from verified sources; reference vectors |
| 3 | Ephemeris engine | DE440 evaluation validated against CSPICE and Horizons |
| 4 | Simulation API and headless host | Clock, orientation, snapshots, orbit paths, presentation math, CLI |
| 5 | Unity MVP | Interactive Solar System viewer (v1) |
| 6 | Moons and visual fidelity | Major moons, full rotation models, sourced textures |
| 7 | Small bodies | Dwarf planets, asteroids, comets |
| 8 | N-body sandbox | Hypothetical scenarios with measured drift |
| 9 | Beyond the Solar System | Stars, exoplanets, galactic scale (outline) |

Version 1 (the Solar System MVP in SPEC section 3.1) is complete at the end of Phase 5.

---

## Phase 0: Repository foundation

**Goal:** a clean monorepo where the core builds and tests outside Unity, and Unity can reference it.

- [x] **P0-01** Initialize the git repository at the project root with `.gitignore` (root and `UniverseSim/`) and `.gitattributes` for Unity and .NET.
  - 2026-10-20: Done. `main` branch. Git LFS not configured (see ADR-0005).
- [x] **P0-02** Write the initial documents: SPEC, ARCHITECTURE, IMPLEMENTATION_PLAN, DATA_SOURCES, ADRs 0001 to 0005, CLAUDE.md, README.
  - 2026-10-20: Done. Source URLs checked as reachable on this date.
- [ ] **P0-03** **(user)** Install the .NET 10 SDK (current LTS). Only SDKs 6.0 and 7.0 were found on the dev machine, and both are out of support. Add `global.json` that pins the SDK feature band.
- [ ] **P0-04** Create `core/UniverseSim.sln`, `core/Directory.Build.props` (nullable enabled, warnings as errors, deterministic, `LangVersion` 9.0 for the core) and `core/src/UniverseSim.Core` targeting `netstandard2.1` with no package references.
- [ ] **P0-05** Create `core/tests/UniverseSim.Core.Tests` (NUnit, `net10.0`) with one smoke test.
- [ ] **P0-06** Create `tools/UniverseSim.Cli` (`net10.0`) that prints the core assembly version.
- [ ] **P0-07** Create `tools/scripts/build-core-for-unity.sh` (and a `.ps1` equivalent) that builds the core in Release and copies the DLL, PDB and XML docs to `UniverseSim/Assets/Plugins/UniverseSim.Core/`. Commit the generated `.dll.meta` (the DLL itself is ignored).
- [ ] **P0-08** Create the Unity assembly `UniverseSim.Unity` (asmdef in `Assets/UniverseSim/Runtime/`) that references the core DLL. Add a trivial MonoBehaviour that logs the core version in the sample scene to prove the integration.
- [ ] **P0-09** **(user)** Decide whether to remove the URP template's tutorial content (`Assets/Readme.asset`, `Assets/TutorialInfo/`) and the sample scene.
- [ ] **P0-10** Add `.editorconfig` with C# style rules shared by the core, the tools and the Unity code.
- [ ] **P0-11** **(user)** Choose a remote host (for example GitHub). Then add CI that runs `dotnet build` and `dotnet test` on push. Validation tests join CI in Phase 3.
- [ ] **P0-12** Fill in the "Commands" section of `CLAUDE.md` and the "Getting started" section of `README.md` with the real commands.

**Acceptance criteria**
- `dotnet build` and `dotnet test` succeed from a clean clone.
- Unity 6000.5.1f1 opens the project and compiles with the core DLL referenced. The sample MonoBehaviour logs the core version.
- The CLI prints the core version.

---

## Phase 1: Core foundations

**Goal:** the building blocks every later phase depends on, fully unit tested.

- [ ] **P1-01** `Mathematics`: `Vector3d`, `Matrix3x3d`, `Quaterniond` (normalization, multiplication, conversion to and from matrices, slerp), angle helpers, and Chebyshev evaluation of a value and its derivative (Clenshaw recurrence).
- [ ] **P1-02** `Constants`: definitional constants only, each with a citation comment and a test. Include the astronomical unit (149,597,870.700 km exactly, IAU 2012 Resolution B2), the speed of light (299,792.458 km/s, SI definition), J2000.0 (JD 2451545.0 TDB), TT - TAI = 32.184 s, seconds per day, and the ECLIPJ2000 obliquity as used by SPICE. Measured quantities (GM, radii and so on) must **not** go here; they come from data packs.
- [ ] **P1-03** `Time`: `Instant` (two-part TDB), Julian date conversions, calendar conversion (Gregorian, Julian, and Horizons-style mixed mode), and ISO 8601 parsing and formatting with an explicit time scale suffix.
- [ ] **P1-04** `Time`: time scale conversions TDB <-> TT <-> TAI <-> UTC. The leap second table is injected as data (loaded from the pack in Phase 2; test fixtures until then). Document the TDB-TT model used. Matching SPICE's model is recommended for parity with the validation oracle.
- [ ] **P1-05** `Frames`: `FrameId`, the ICRF <-> ECLIPJ2000 rotation, composition of frame transforms, and a placeholder for body-fixed frames (completed in P4-03).
- [ ] **P1-06** `Catalog`: `BodyId` (namespace + value), `BodyKind`, `Body`, `BodyCatalog` (tree with primary links), `Quantity` (value, unit, uncertainty, `SourceRef`, derived flag, explicit missing state) and `Source`.
- [ ] **P1-07** `Presentation`: `AxisConvention` for Unity (left-handed, Y up) and Godot (right-handed, Y up), for vectors and quaternions. Verify the quaternion mapping against matrix conversion over random rotations (see ARCHITECTURE section 5.4).
- [ ] **P1-08** XML documentation on every public member, stating units, frame and time scale where relevant. Enable the documentation file output.

**Acceptance criteria**
- Unit test coverage on these namespaces is meaningful (edge cases: calendar reform dates, leap second boundaries, negative Julian dates, quaternion sign ambiguity).
- No public API without XML docs (enforced by the compiler warning CS1591 being treated as an error).

---

## Phase 2: Data pipeline and data pack

**Goal:** a reproducible path from verified sources to a versioned data pack, plus independent reference data for validation.

- [ ] **P2-01** Create `tools/UniverseSim.DataPipeline` (`net10.0`) with the commands `fetch`, `verify`, `build` and `info` (see ARCHITECTURE section 4).
- [ ] **P2-02** Write `data/sources.json` (the declared sources from DATA_SOURCES.md needed for v1: `de440s.bsp`, `pck00011.tpc`, `gm_de440.tpc`, `naif0012.tls`). Implement `fetch` with HTTPS download, size check, publisher MD5 check (from NAIF `aa_checksums.txt` where available) and SHA-256, writing `data/sources.lock.json`.
- [ ] **P2-03** DAF reader and SPK segment readers for types 2 and 3, in the core (`Ephemeris` namespace). Follow the NAIF "DAF Required Reading" and "SPK Required Reading". Handle both endiannesses (the DAF file record declares the binary format).
- [ ] **P2-04** Text kernel parser in the pipeline for `\begindata` blocks (PCK, LSK, GM files): numbers with `D` exponents, arrays, continuation lines, and `@` date tokens.
- [ ] **P2-05** Catalog builder for v1 bodies: SSB, Sun, the planet system barycenters, the planets, the Moon and Pluto. It takes radii and rotation elements from `pck00011.tpc`, GM from `gm_de440.tpc` and names and IDs from NAIF. Every field gets a `SourceRef` (file plus keyword). Derived values (for example mean radius computed from triaxial radii) record their formula.
- [ ] **P2-06** Manual entry mechanism: `data/catalog/manual/*.json` with a schema requiring source id, URL, access date, table or section, and transcriber note. The pipeline rejects entries missing any of these. Use it only when no machine-readable Tier 1 source exists.
- [ ] **P2-07** Data pack format v1: write `docs/formats/DATA_PACK.md`, implement the writer (pipeline) and the reader (core `Data` namespace). Support trimming ephemeris segments to a time window. Embed the source table and the lock file hash.
- [ ] **P2-08** Reproducibility test: building twice from the same lock file yields identical bytes. Never embed wall-clock time in the content hash.
- [ ] **P2-09** `tools/reference/` (Python, `requirements.txt` with pinned SpiceyPy): scripts that generate reference vectors from the same kernels (states, orientations, time conversions) and capture Horizons API vectors. Commit the outputs to `data/reference/` with provenance headers.
- [ ] **P2-10** **(user)** Decide the default pack time window (for example 1900 to 2100, or the full de440s span) and how large data is distributed (OQ-02). Record the decision in an ADR.

**Acceptance criteria**
- From a clean clone: `fetch` then `build` produces a pack. All checksums verify. `info` lists bodies, coverage and sources.
- The pack loads in the core and the catalog values match the source kernels exactly.
- Reference vector files exist for VAL-01 to VAL-04.

---

## Phase 3: Ephemeris engine

**Goal:** positions and velocities of v1 bodies from DE440, validated.

- [ ] **P3-01** Chebyshev segment evaluation: type 2 (position, velocity from the derivative) and type 3 (position and velocity).
- [ ] **P3-02** Record lookup by time within a segment (direct index from the fixed record interval) and segment selection across files and windows. Report coverage per body (FR-SIM-03).
- [ ] **P3-03** `EphemerisChain`: resolve any target and center pair through the segment graph (common ancestor). Cache paths.
- [ ] **P3-04** Output frames: ICRF natively, ECLIPJ2000 by rotation.
- [ ] **P3-05** Out-of-coverage handling: return an explicit `OutOfCoverage` result, never extrapolate.
- [ ] **P3-06** Performance: a benchmark project (BenchmarkDotNet) for a full v1 snapshot. Meet NFR-03 (under 0.1 ms, zero allocations).
- [ ] **P3-07** Validation tests VAL-01 (against CSPICE) and VAL-02 (against Horizons). Measure the DE440 vs Horizons (DE441) differences, record them here, and set the final VAL-02 threshold.
- [ ] **P3-08** Add validation tests to CI. The pack must be built in CI from the lock file, or cached.

**Acceptance criteria**
- VAL-01 and VAL-02 pass.
- A state query from the CLI (for example Earth relative to the Sun at a given TDB instant) matches Horizons within the threshold.

---

## Phase 4: Simulation API and headless host

**Goal:** the complete engine-agnostic API a renderer needs, proven with the CLI.

- [ ] **P4-01** `SimulationClock`: current instant, signed rate, pause, set time, and clamping to the coverage of the loaded pack, with events or flags when clamped.
- [ ] **P4-02** `Simulation` facade: load a pack, expose the catalog, `GetState`, `GetOrientation`, and the zero-allocation `FillSnapshot` (ARCHITECTURE section 3.3).
- [ ] **P4-03** Body orientation from IAU rotation models (pole right ascension and declination, prime meridian, including the periodic nutation and precession terms in `pck00011.tpc`). Validate with VAL-04.
- [ ] **P4-04** `OrbitPathSampler`: adaptive sampling (denser near periapsis) of a body's path relative to its primary over one orbital period or a given window. Cache results and invalidate them on large time jumps.
- [ ] **P4-05** `ViewOrigin` and scale modes (`TrueScale`, `EnlargedBodies`): positions re-centered on the view origin and scaled to render units, in double precision.
- [ ] **P4-06** CLI commands `catalog`, `state`, `orientation`, `snapshot` and `orbit`, with JSON and CSV output.
- [ ] **P4-07** Write `docs/API.md`: a short guide for host authors (the frame loop, the precision strategy, the axis conventions, error handling).

**Acceptance criteria**
- The CLI produces a full snapshot and orbit paths for all v1 bodies.
- VAL-04 passes.
- No engine is involved anywhere in Phases 1 to 4.

---

## Phase 5: Unity MVP (completes v1)

**Goal:** an interactive Solar System viewer driven entirely by the core.

- [ ] **P5-01** `SimulationHost` MonoBehaviour: loads the pack from `StreamingAssets`, owns the `Simulation`, advances the clock and fills the snapshot each frame. Add the `copy-pack-to-unity` script.
- [ ] **P5-02** `BodyView` prefab and spawning from the catalog: a sphere scaled by radius (triaxial where the data provides it) and oriented from the snapshot.
- [ ] **P5-03** Floating origin: apply the snapshot positions relative to the double-precision view origin. Test for jitter at Earth surface zoom and at Pluto distance.
- [ ] **P5-04** Depth strategy across scales (reversed-Z, dynamic clip planes or layered cameras). Document the choice in an ADR.
- [ ] **P5-05** Camera rig with Input System actions: focus and orbit a body, logarithmic zoom, smooth focus transitions across scales, and free-fly.
- [ ] **P5-06** Sun lighting at any scale: lit shader or per-body lighting driven by the Sun direction from the core. Correct phases for the Moon and planets.
- [ ] **P5-07** Time controls UI (UI Toolkit): play and pause, signed rate presets, date and time entry with the time scale shown, a "now" button, and a coverage indicator.
- [ ] **P5-08** Body info panel: values with units, uncertainty when available, and source citations. Display fallbacks are visibly marked.
- [ ] **P5-09** Orbit lines from `OrbitPathSampler` (camera-relative) and screen-space labels, each with a toggle.
- [ ] **P5-10** Scale mode toggle with a persistent on-screen indicator.
- [ ] **P5-11** Visual assets: textures from verified sources (DATA_SOURCES.md, visual section), with `ATTRIBUTION.md`.
- [ ] **P5-12** Unity tests: EditMode tests for snapshot-to-transform mapping, and a PlayMode smoke test that compares a body's position with CLI output for the same time.

**Acceptance criteria**
- Opening the main scene shows the Solar System at the current date and time.
- UC-01 to UC-06 work.
- Positions in Unity match the CLI for the same instant, after the documented axis mapping and scaling.
- There are no visible precision artifacts at any supported zoom.

---

## Phase 6: Moons and visual fidelity

- [ ] **P6-01** Select satellite SPKs for the major moons (Phobos, Deimos, Galilean moons, major Saturnian moons, major Uranian moons, Triton, Charon) using NAIF `aa_summaries.txt`. Pin them in the lock file. Trim them to the pack window and keep the pack within its size budget.
- [ ] **P6-02** Inspect the segment types of the selected kernels and implement any SPK types beyond 2 and 3 that they need.
- [ ] **P6-03** Rotation models for the moons (IAU, including synchronous rotation where the report defines it).
- [ ] **P6-04** Saturn's rings and other ring systems: dimensions from a Tier 1 source, rendered as visual-only geometry.
- [ ] **P6-05** Optional: high-precision lunar orientation from `moon_pa_de440_200625.bpc` (binary PCK; needs DAF PCK type 2 support).
- [ ] **P6-06** Optional: high-precision Earth orientation from `earth_latest_high_prec.bpc`.
- [ ] **P6-07** Textures for the moons with attribution.

---

## Phase 7: Small bodies

- [ ] **P7-01** SBDB API ingestion for the dwarf planets (Ceres, Eris, Haumea, Makemake and others) and for selected asteroids and comets. Store elements, epoch, uncertainties and provenance.
- [ ] **P7-02** Where a Horizons-generated SPK is available for a body, prefer it (ephemeris model) over Kepler propagation.
- [ ] **P7-03** Two-body Kepler propagator (elliptic, parabolic and hyperbolic, using universal variables). Results are flagged `Analytic`.
- [ ] **P7-04** Test-particle integrator in the gravity field of the ephemeris bodies. Use the GM values from `gm_de440.tpc`, with optional asteroid perturbers.
- [ ] **P7-05** VAL-05: compare against Horizons and report errors per body.
- [ ] **P7-06** Unity: render many small bodies (GPU instancing or point sprites) with a level-of-detail scheme.

---

## Phase 8: N-body sandbox

- [ ] **P8-01** Integrator interface. Implement DOP853 (Hairer, Norsett and Wanner) and IAS15 (Rein and Spiegel 2015, MNRAS 446, 1424).
- [ ] **P8-02** Force models: Newtonian point mass, the 1PN (Einstein-Infeld-Hoffmann) relativistic correction, and optionally J2 oblateness.
- [ ] **P8-03** Initialize from ephemeris states at a chosen epoch. Run VAL-06 (drift against DE440) and VAL-07 (conservation), and set thresholds based on the measurements.
- [ ] **P8-04** Scenario files describing modifications (mass changes, added or removed bodies) on top of a sourced base state. Every resulting state is flagged hypothetical.
- [ ] **P8-05** Unity: sandbox UI with an unmistakable "hypothetical" indicator.

---

## Phase 9: Beyond the Solar System (outline)

To be broken down into tasks when Phase 5 is complete.

- **P9-01** Hierarchical frames and simulation domains (galactic frame; star system local frames anchored at barycenters).
- **P9-02** Stellar neighborhood from Gaia DR3 (for example the Gaia Catalogue of Nearby Stars) and the Hipparcos new reduction: positions, parallaxes, proper motions and radial velocities, with a linear space motion model and honest treatment of low-significance parallaxes.
- **P9-03** Star rendering: magnitudes and color indices mapped to brightness and color. Sky background generated from the catalog.
- **P9-04** Exoplanet systems from the NASA Exoplanet Archive, with uncertainties and missing parameters left missing.
- **P9-05** Research spike on galactic and extragalactic scale data and models.

---

## Cross-cutting tasks (any phase)

- [ ] **X-01** Keep `CLAUDE.md`, `README.md` and the documents in `docs/` in sync with the code.
- [ ] **X-02** Every new data source: add it to `DATA_SOURCES.md` and `data/sources.json` before using it.
- [ ] **X-03** **(user)** Choose a license for the project (OQ-05).
