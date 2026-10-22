# Implementation Plan

| | |
|---|---|
| Status | Active |
| Last updated | 2026-10-22 |
| Current phase | Phase 6: moons, rings and the starry sky done (P6-01 to P6-04, P6-08); open: P6-05, P6-06 (optional PCKs, downloaded, not registered) and P6-07 (textures). Phase 5 done except the owner's hands-on input check. |
| Related | [SPEC.md](SPEC.md), [ARCHITECTURE.md](ARCHITECTURE.md), [DATA_SOURCES.md](DATA_SOURCES.md), [adr/](adr/) |

## How to use this plan

- Work proceeds phase by phase. A phase is done when all its tasks are checked **and** its acceptance criteria are met.
- Tasks have stable IDs (`P<phase>-<nn>`). Reference them in commits and notes. Do not renumber; append new tasks at the end of a phase.
- When you finish a task: check its box and add a dated note under it (one or two lines on what was done and any deviation). Update "Current phase" above when a phase completes.
- When a task requires a decision that changes [SPEC.md](SPEC.md) or [ARCHITECTURE.md](ARCHITECTURE.md), write an ADR and update those documents in the same change.
- Tasks marked **(user)** need the user's input or action before or during the work.
- Tasks marked **(deferred)** are intentionally postponed. They do not block their phase. Each carries a note saying why and when to revisit it.
- Requirement IDs (FR-*, NFR-*, VAL-*) refer to [SPEC.md](SPEC.md).

## Phase overview

| Phase | Name | Outcome |
|---|---|---|
| 0 | Repository foundation | Repo, docs, toolchain, C++ skeleton, Unity loads the native core |
| 1 | Core foundations | Math, time, frames, catalog model, axis conventions, error model |
| 2 | Data pipeline and data pack | Reproducible Python pipeline from verified sources; pack reader in C++; reference vectors |
| 3 | Ephemeris engine | DE440 evaluation validated against CSPICE and Horizons |
| 4 | Simulation API, C ABI and headless host | Clock, orientation, snapshots, orbit paths, presentation math, C ABI, CLI |
| 5 | Unity MVP | Interactive Solar System viewer (v1) |
| 6 | Moons and visual fidelity | Major moons, full rotation models, sourced textures |
| 7 | Small bodies | Dwarf planets, asteroids, comets |
| 8 | N-body sandbox | Hypothetical scenarios with measured drift |
| 9 | Beyond the Solar System | Stars, exoplanets, galactic scale (outline) |

Version 1 (the Solar System MVP in SPEC section 3.1) is complete at the end of Phase 5.

---

## Phase 0: Repository foundation

**Goal:** a clean monorepo where the core builds and tests outside Unity, and Unity loads it as a native plugin.

- [x] **P0-01** Initialize the git repository at the project root with `.gitignore` (root and `UniverseSim/`) and `.gitattributes` for Unity and C++.
  - 2026-10-20: Done. Branch `main`. Git LFS tracking rules added for binary assets (ADR-0005).
- [x] **P0-02** Write the initial documents: SPEC, ARCHITECTURE, IMPLEMENTATION_PLAN, DATA_SOURCES, ADRs 0001 to 0005, CLAUDE.md, README.
  - 2026-10-20: Done. Source URLs checked as reachable. Revised the same day when the project owner chose C++ for the core (ADR-0002 rewritten; ADR-0005 updated for the Python pipeline and Git LFS).
- [x] **P0-03** Toolchain: Git LFS, CMake 3.25 or newer, Ninja, a C++20 compiler.
  - 2026-10-20: Installed with Homebrew: git-lfs 3.8.0 (`git lfs install` run), CMake 4.4.4, Ninja 1.13.2. Compiler: Apple Clang 21 (Xcode).
- [x] **P0-04** CMake project in `core/`: `usim_core` (static), `universesim` (shared, C ABI), shared compiler options (warnings as errors, `-ffp-contract=off`), presets `dev`, `release` and `unity-macos`.
  - 2026-10-20: Done. Only `USIM_API` symbols are exported (hidden visibility by default).
- [x] **P0-05** Unit tests with Catch2, pinned by version and SHA-256, discovered by `ctest`.
  - 2026-10-20: Catch2 v3.16.0 (v3.16.1 was under two weeks old). 4 tests pass (version and C ABI).
- [x] **P0-06** CLI host `usim` that prints the core version.
  - 2026-10-20: `core/build/dev/apps/usim-cli/usim --version` prints `usim 0.1.0`.
- [x] **P0-07** `tools/scripts/build-core-for-unity.sh`: builds the plugin and copies it into `UniverseSim/Assets/UniverseSim/Plugins/<platform>/`.
  - 2026-10-20: macOS done. Universal binary (arm64 + x86_64), deployment target 12.0 to match the Unity 6000.5 player (`UnityPlayer.dylib` reports `minos 12.0`). The plugin lives under `Assets/UniverseSim/Plugins/` rather than `Assets/Plugins/`, so ignoring it leaves no orphan folder `.meta` files.
- [x] **P0-08** Unity integration: asmdefs `UniverseSim.Unity`, `UniverseSim.Unity.Editor` and `UniverseSim.Unity.Tests.EditMode`; P/Invoke bindings (`NativeMethods`, `CoreLibrary`); `CoreStartupCheck`; `NativePluginImportSettings`.
  - 2026-10-20: Verified headless with `-batchmode -runTests`: 3 of 3 EditMode tests pass (library loads, ABI matches, import settings correct). Found and fixed a first-import race: Unity imported the plugin before compiling the postprocessor, so an `[InitializeOnLoad]` pass now corrects settings after each domain reload.
- [x] **P0-09** Remove the URP template's tutorial content and set up the main scene.
  - 2026-10-20: The project owner gave blanket approval for changes inside the Unity project. Removed `Assets/Readme.asset` and `Assets/TutorialInfo/`, which referenced only each other. Moved `Assets/Scenes/SampleScene.unity` to `Assets/UniverseSim/Scenes/Main.unity` with `git mv` (GUID kept) and updated the build settings. Kept the volume profile (referenced by the render pipeline assets) and `InputSystem_Actions.inputactions` (the project-wide actions). Added `ProjectSetupTests`. 5 of 5 EditMode tests pass.
- [x] **P0-10** `.editorconfig` and `.clang-format` at the repository root.
  - 2026-10-20: Done. clang-format itself is not installed (optional: `brew install clang-format`).
- [ ] **P0-11** **(deferred)** Choose a remote host, then add CI that builds and runs `ctest` (and later the validation tests).
  - 2026-10-20: Deferred by the project owner; CI is not needed yet. Until then, run the commands in CLAUDE.md locally before declaring a task done.
- [x] **P0-12** Fill in the commands in `CLAUDE.md` and `README.md`.
  - 2026-10-20: Done.
- [ ] **P0-13** **(deferred)** Windows plugin: a Windows x64 build of `universesim.dll`, a Windows branch in `NativePluginImportSettings`, and the build script extended to produce it.
  - 2026-10-20: Deferred; development happens on macOS, and Windows is needed only to run the viewer on Windows. No Windows machine is required to do this work: the DLL can be cross-compiled on macOS (for example with mingw-w64 from Homebrew, or clang-cl with the Windows SDK), and its exports can be checked there. Only the final check, Unity on Windows loading the DLL, needs Windows. Revisit when a Windows build is wanted.
- [x] **P0-14** Sanitizer preset (`asan`: AddressSanitizer and UndefinedBehaviorSanitizer) for local runs and later CI.
  - 2026-10-20: Done. Tests pass under `ctest --preset asan`.
- [x] **P0-15** Python 3.12 or newer for the Phase 2 pipeline.
  - 2026-10-20: Already installed (pyenv 3.12.8, native arm64). Pinned for this repository with `.python-version` (`pyenv local 3.12.8`), so `python3` in the repo resolves to it. The global pyenv default (3.9.13) is unchanged.

**Acceptance criteria** (all met on macOS, 2026-10-20)
- `cmake --preset dev && cmake --build --preset dev && ctest --preset dev` succeeds from a clean clone.
- Unity 6000.5.1f1 loads the native core, and the EditMode tests pass.
- The CLI prints the core version.

---

## Phase 1: Core foundations

**Goal:** the building blocks every later phase depends on, fully unit tested. All code is in `core/include/usim/` and `core/src/`.

- [x] **P1-01** `usim::math`: `Vec3`, `Mat3`, `Quat` (normalization, multiplication, conversion to and from matrices, slerp), angle helpers, and Chebyshev evaluation of a value and its derivative (Clenshaw recurrence).
  - 2026-10-20: Done. Quaternions use the SPICE convention (scalar first, Hamilton, active rotation; `from_matrix` returns w >= 0), checked against CSPICE `axisar` and `m2q`. Chebyshev value and derivative agree with CSPICE `chbint` to 1.8e-15.
- [x] **P1-02** `usim/constants.hpp`: definitional constants only, each with a citation comment and a test. Include:
  - the astronomical unit (149,597,870.700 km exactly, IAU 2012 Resolution B2)
  - the speed of light (299,792.458 km/s, SI definition)
  - J2000.0 (JD 2451545.0 TDB)
  - TT - TAI = 32.184 s
  - seconds per day
  - the ECLIPJ2000 obliquity as used by SPICE

  Measured quantities (GM, radii and so on) must **not** go here; they come from data packs.
  - 2026-10-20: Done, plus the light-year and the Julian year and century. The obliquity (84381.448 arcsec) reproduces CSPICE `pxform("J2000", "ECLIPJ2000")` to 2e-16.
- [x] **P1-03** `usim::time`: `Instant` (two-part TDB), Julian date conversions, calendar conversion (Gregorian, Julian, and Horizons-style mixed mode), and ISO 8601 parsing and formatting with an explicit time scale suffix.
  - 2026-10-20: Done. Deviation: `Instant` is `TimePoint<TimeScale::tdb>`, with `TtTime` and `TaiTime` as distinct types of the same template, so scales cannot be mixed up. Julian day numbers match CSPICE for 24 dates (4714 B.C. to 9999) in each of the three calendars. Unlike CSPICE, the mixed calendar rejects 1582-10-05 to 1582-10-14.
- [x] **P1-04** `usim::time`: conversions TDB <-> TT <-> TAI <-> UTC. The leap second table is injected as data (loaded from the pack in Phase 2; test fixtures until then). Document the TDB-TT model used. Matching SPICE's model is recommended for parity with the validation oracle.
  - 2026-10-20: Done with the SPICE DELTET model; its parameters are injected too (`TdbModel`), so no measured value is typed into the core. Test data comes from `naif0012.tls` through `tools/scripts/generate_spice_test_fixtures.py` (SpiceyPy 8.2.0, CSPICE N0067) into `core/tests/fixtures/spice_reference.hpp`. Measured against CSPICE at the fixture epochs: TDB <-> TT identical to `unitim` as doubles in both directions, TAI within 3.3e-12 s, UTC -> TDB within 2.1e-12 s of `str2et`, and `et2utc` text identical. Finding: CSPICE's UTC string parser rounds decimal fractions (".816") with errors up to 1.7e-9 s, so the fixture epochs use binary fractions. UTC before 1972 fails with `out_of_range` (CSPICE extrapolates).
- [x] **P1-05** `usim::frames`: `FrameId`, the ICRF <-> ECLIPJ2000 rotation, composition of frame transforms, and a placeholder for body-fixed frames (completed in P4-03).
  - 2026-10-20: Done. `FrameTransform` carries R and dR/dt (like SPICE `sxform`) so rotating frames slot in later. `FrameSystem` returns `unsupported` for body-fixed frames until P4-03.
- [x] **P1-06** `usim::catalog`: `BodyId` (namespace + value), `BodyKind`, `Body`, `BodyCatalog` (tree with primary links), `Quantity` (optional value, unit, uncertainty, `SourceRef`, derived flag) and `Source`.
  - 2026-10-20: Done. The derived flag is a `derivation` string (formula and inputs; empty means not derived). `BodyCatalog::create` rejects duplicates, missing primaries, cycles, non-finite values and values citing unregistered sources. `Body` has GM, mean radius and the triaxial radii; more fields come with the pack format (P2-07).
- [x] **P1-07** `usim::presentation::AxisConvention` for Unity, Godot and Unreal, for vectors and quaternions. Test against matrix conversion over random rotations (formulas in ARCHITECTURE section 5.4).
  - 2026-10-20: Done, with `from_host` inverses. 2000 random rotations per convention match S R S^T to 1e-15.
- [x] **P1-08** Error model: a `Result<T>` type (value or error code plus message) and an error code enum that maps one-to-one to the C ABI status codes. No exceptions in the core's public API.
  - 2026-10-20: Done in `usim/result.hpp` (namespace `usim`, not `usim::sim`). Added `USIM_STATUS_*` and `usim_status_name()` to the C ABI (additive, ABI version stays 1), static assertions tying them to `ErrorCode`, and a C# `Status` enum checked against the library in a Unity test (6 of 6 EditMode tests pass).
- [x] **P1-09** Doc comments (`///`) on every public declaration, stating units, frame and time scale where relevant.
  - 2026-10-20: Done. SPEC NFR-09 said "XML documentation" (left over from C#); corrected to `///` comments.

**Acceptance criteria**
- Unit tests cover the edge cases: calendar reform dates, leap second boundaries, negative Julian dates, the quaternion sign ambiguity.
- The build is warning-free with warnings as errors, and the tests pass under the `asan` preset.

Met on 2026-10-20: 71 tests pass under the `dev`, `release` and `asan` presets with no warnings.

---

## Phase 2: Data pipeline and data pack

**Goal:** a reproducible path from verified sources to a versioned data pack, plus independent reference data for validation. Uses Python 3.12.8 (pinned in `.python-version`) with a virtual environment in `tools/pipeline/.venv`.

- [x] **P2-01** `tools/pipeline/`: a Python package (`pyproject.toml`, pinned `requirements.txt`, pytest) with the commands `fetch`, `verify`, `build`, `info` and `reference` (ARCHITECTURE section 4).
  - 2026-10-21: Done. Package `usim_pipeline`, run through `tools/pipeline/usim-pipeline`. `requirements.txt` pins every package by version and by the SHA-256 of every PyPI file (`pin_requirements.py`; `pip install --require-hashes`): SpiceyPy 8.2.0, numpy 2.5.3, pytest 9.1.1 and its dependencies, each at least two weeks old. 87 pytest tests.
- [x] **P2-02** Write `data/sources.json` with the v1 sources from DATA_SOURCES.md: `de440s.bsp`, `pck00011.tpc`, `gm_de440.tpc` and `naif0012.tls`. Implement `fetch`:
  - HTTPS download with a size check
  - publisher MD5 check (from NAIF `aa_checksums.txt` where available)
  - SHA-256
  - writes `data/sources.lock.json`
  - 2026-10-20: Done, plus `S-NAIF-IDS` and `S-JPL-HORIZONS` (no files). Only `spk/planets` publishes an MD5 list (`de440s.bsp` verified); `pck` and `lsk` have none. An existing lock entry is never changed silently: a different download fails until `fetch --update`. Tested with a fake server (MD5 and size mismatches, publisher changes, stale entries).
- [x] **P2-03** Segment extraction for SPK types 2 and 3 through SpiceyPy (the DAF functions). Record the layout of the extracted records (init, interval length, coefficients) in the pack specification.
  - 2026-10-20: Done (`dafopr`, `dafgs`, `dafus`, `dafgda`); layout in DATA_PACK.md, section EPHS. A test compares the pack's records with the raw bytes of the SPK file, read without SpiceyPy. Finding: `de440s.bsp` has no segments for the centers of Mars to Pluto, only their barycenters (OQ-08, P3-09).
- [x] **P2-04** Constants from text kernels through the SpiceyPy kernel pool (`furnsh`, `gdpool`, `bodvrd`). Every value records its file and keyword.
  - 2026-10-20: Done with a deviation (ADR-0007). Finding: CSPICE's text kernel parser is not correctly rounded (36 of 227 values in `gm_de440.tpc` and 9 of 2896 in `pck00011.tpc` differ from their text by 1 to 5 ulps). The pack keeps the correctly rounded value of the text, parsed by the pipeline and cross-checked against the kernel pool within 16 ulps (measured maximum 5). Kernels are loaded one at a time, so each value's file is certain.
- [x] **P2-05** Catalog builder for the v1 bodies: SSB, Sun, the planet system barycenters, the planets, the Moon and Pluto.
  - Radii and rotation elements come from `pck00011.tpc`.
  - GM comes from `gm_de440.tpc`.
  - Names and IDs come from NAIF.
  - Derived values (for example mean radius from the triaxial radii) record their formula.
  - 2026-10-20: Done. Selection in `data/catalog/bodies-v1.json` (21 bodies; the Moon's primary is the Earth-Moon barycenter, mirroring the ephemeris, SPEC 7.5). 64 values: GM for 20 bodies (the file gives none for the SSB), radii and the derived mean radius for 11; IAU rotation elements for 11, including the quadratic Mars phase angles. The mean radius is cbrt(a b c), computed in 50-digit decimal arithmetic so no platform libm affects the bytes. Names come from CSPICE `bodc2n`, title-cased (new registry entry S-NAIF-IDS).
- [x] **P2-06** Manual entry mechanism: `data/catalog/manual/*.json` with a schema requiring source id, URL, access date, table or section, and transcriber note. The pipeline rejects entries missing any of these. Use it only when no machine-readable Tier 1 source exists.
  - 2026-10-20: Done (`usim_pipeline/manual.py`, schema in its docstring). Also rejected: undeclared sources, wrong units, unknown keys, and entries for a field a kernel already provides. v1 needs no manual entries.
- [x] **P2-07** Data pack format v1:
  - Write `docs/formats/DATA_PACK.md`.
  - Implement the writer in Python and the reader in C++ (`usim::data`).
  - Support trimming ephemeris segments to a time window.
  - Embed the source table and the lock file hash.
  - 2026-10-21: Done. `usim::data::DataPack` validates everything and verifies the content hash with a new C++ SHA-256 (FIPS 180-4 examples and hashlib vectors). It also carries the source files from the lock, IAU rotation elements (`usim::frames::IauRotationElements`, data only until P4-03) and the time data. The lock hash is over canonical JSON, so line endings do not change it. Default pack: 32,681,168 bytes; it loads in about 0.17 s (release build, SHA-256 included).
- [x] **P2-08** Reproducibility test: building twice from the same lock file yields identical bytes. No wall-clock time in the content hash.
  - 2026-10-21: Done. A pytest builds twice and compares; another checks the committed default pack equals a fresh build. A simulated clean clone (fresh downloads into an empty root) built the identical file.
- [x] **P2-09** Round-trip test: a small synthetic pack written by the pipeline is committed as a test fixture (`core/tests/fixtures/`), and the C++ reader tests load it.
  - 2026-10-21: Done. `test_pack_v1.uspack` (3592 bytes, Git LFS) and `test_pack_expected.hpp` (`usim-pipeline reference test-pack`). The C++ tests compare every field bit for bit and check that damaged packs are rejected.
- [x] **P2-10** `reference` command: reference vectors from CSPICE on the same kernels (states, orientations, time conversions) and Horizons API captures, committed to `data/reference/` with provenance headers. It also takes over `tools/scripts/generate_spice_test_fixtures.py` (the Phase 1 unit test oracle) and keeps producing `core/tests/fixtures/spice_reference.hpp`.
  - 2026-10-21: Done. VAL-01: 966 states for 21 target and center pairs (`spkgeo`). VAL-03: 182 UTC and 101 TDB epochs. VAL-04: 341 orientations with rates (`sxform`). VAL-02: 14 Horizons captures (barycenters, Sun, Mercury, Venus, Earth, Moon relative to the SSB, 1900 to 2100 every 397 days; DE441). Also `pack_kernel_values.csv`, the kernel text of every pack value. The old generator script was removed; its output is identical apart from the first line. VAL-03 already passes against the pack's time data: at most 4.9e-7 s, the resolution of CSPICE's one-double TDB seconds near 2150.
- [x] **P2-11** **(user)** Decide the default pack time window (for example 1900 to 2100, or the full de440s span) and whether a prebuilt default pack is committed through Git LFS (OQ-02). Record the decision in an ADR.
  - 2026-10-20: The project owner chose the full `de440s.bsp` span and committing the default pack through Git LFS (ADR-0006).

**Acceptance criteria**
- From a clean clone: `fetch` then `build` produces a pack. All checksums verify. `info` lists bodies, coverage and sources.
- The C++ reader loads the pack, and the catalog values match the source kernels exactly.
- Reference vector files exist for VAL-01 to VAL-04.

Met on 2026-10-21: a simulated clean clone ran `fetch` (all files matched the lock), `verify`, `build` (identical to the committed pack) and `info`. The validation tests confirm every catalog, rotation and time value equals the kernel text exactly. Reference files exist for VAL-01 to VAL-04. 85 core tests pass under `dev`, `release` and `asan`; 87 pipeline tests pass.

---

## Phase 3: Ephemeris engine

**Goal:** positions and velocities of v1 bodies from DE440, validated.

- [x] **P3-01** Chebyshev segment evaluation in C++: type 2 (position, velocity from the derivative) and type 3 (position and velocity).
  - 2026-10-21: Done (`usim::ephemeris::ChebyshevSegment`, a view of a pack segment). The argument (t - MID) / RADIUS is computed from the two parts of the `Instant`. Creating the view checks that every record spans the instants it serves.
- [x] **P3-02** Record lookup by time within a segment (direct index from the fixed record interval), and segment selection across windows. Report coverage per body (FR-SIM-03).
  - 2026-10-21: Done. Lookup as in SPICE (the last instant uses the last record); the later overlapping segment wins. `Coverage` holds disjoint closed TDB intervals; `EphemerisChain::coverage` gives it per body (to its root) and per pair.
- [x] **P3-03** `EphemerisChain`: resolve any target and center pair through the segment graph (common ancestor). Precompute paths.
  - 2026-10-21: Done. Paths, depths and coverages are computed once; queries walk up from the deeper body to the common ancestor (no barycentric detour). Per OQ-08 (ADR-0008), bodies the pack places at their primary are linked to it with a zero offset and flagged approximate. The pack format gained section APPX (1.1); the default pack is now `solar-system-1.1.0.uspack`.
- [x] **P3-04** Output frames: ICRF natively, ECLIPJ2000 by rotation.
  - 2026-10-21: Done. Segments given in ECLIPJ2000 are rotated into the ICRF; body-fixed output returns `unsupported` until P4-03.
- [x] **P3-05** Out-of-coverage handling: return an explicit `OutOfCoverage` result, never extrapolate.
  - 2026-10-21: Done. `ErrorCode::out_of_coverage`, naming the body whose segments do not cover the instant; invalid instants never evaluate.
- [x] **P3-06** Performance:
  - Catch2 benchmarks for a full v1 snapshot.
  - A test that counts heap allocations (replaced global `operator new`) to prove the snapshot path allocates nothing.
  - Meet NFR-03.
  - 2026-10-21: Done. `EphemerisChain::evaluate` is the allocation-free path (`usim_allocation_tests`, on the synthetic and the default pack, including failing queries). `usim_benchmarks` (ctest label `benchmark`, release builds only): about 4 µs for a snapshot of all 21 bodies in ECLIPJ2000, against the 0.1 ms budget.
- [x] **P3-07** Validation tests VAL-01 (against CSPICE) and VAL-02 (against Horizons), labeled `validation` in ctest. Measure the DE440 vs Horizons (DE441) differences, record them here, and set the final VAL-02 threshold.
  - 2026-10-21: Done. VAL-01: 966 states of 21 pairs agree with `spkgeo` within 1.1e-6 km (the resolution of a double near the Pluto barycenter) and 1.4e-14 km/s. VAL-02 against DE441, 1900 to 2100: the Sun, planets and barycenters within 2 mm, the Earth within 0.13 m, the Moon within 10.3 m (the DE440/DE441 lunar difference). Final threshold (tightened from the provisional 1 km): 1 m, and 20 m for the Moon (SPEC section 9). The Earth relative to the Sun agrees within 0.13 m (the acceptance check).
- [x] **P3-08** Make the validation tests easy to run locally (one command that builds the pack if missing and runs `ctest -L validation`). Add them to CI once CI exists (P0-11).
  - 2026-10-21: Done: `tools/scripts/run-validation.sh [preset]`. An LFS pointer is checked out from the local LFS cache or pulled (tested on a clone without LFS content); a missing pack is built with `fetch` and `build`. CI remains with P0-11.
- [x] **P3-09** **(user)** Decide OQ-08 before P3-03: `de440s.bsp` has no segments for the centers of Mars to Pluto. Either add the planet-center segments from the JPL satellite ephemerides (S-NAIF-SAT; large downloads, trimmed into the pack) or show those planets at their system barycenter, flagged as approximate (FR-SIM-03, FR-SIM-04). Record the decision in an ADR.
  - 2026-10-21: The project owner chose system barycenters, flagged approximate, until Phase 6 (ADR-0008). Measured from the satellite kernels: the centers lie up to 0.21 m (Mars), 220 km (Jupiter), 311 km (Saturn), 44 km (Uranus), 74 km (Neptune) and 2132 km (Pluto) from their barycenters; adding them would grow the pack to about 197 MB.

**Acceptance criteria**
- VAL-01 and VAL-02 pass.
- `usim state` (P4-06) or a test harness shows Earth relative to the Sun at a given TDB instant matching Horizons within the threshold.

Met on 2026-10-21: VAL-01 and VAL-02 pass with their final thresholds. A validation test computes the Earth relative to the Sun at the 185 Horizons epochs and matches the difference of the two Horizons captures within 0.13 m (threshold 1 m). 138 core tests pass under `dev`, `release` (with the benchmark) and `asan`; 100 pipeline tests pass.

---

## Phase 4: Simulation API, C ABI and headless host

**Goal:** the complete engine-agnostic API a renderer needs, exposed through the C ABI and proven with the CLI.

- [x] **P4-01** `SimulationClock`: current instant, signed rate, pause, set time, and clamping to the pack's coverage, with flags when clamped.
  - 2026-10-21: Done (`usim::sim::SimulationClock`). A change that would cross a limit stops there and reports `ClockLimit::start` or `end`; rate and pause are kept. The limits are passed in; the facade (P4-02) derives them from the coverage. Time advances on the two-part `Instant`, so a million 1/60 s steps stay exact to well under a microsecond.
- [x] **P4-02** `Simulation` facade: load a pack, expose the catalog, `state`, `orientation`, and the allocation-free `fill_snapshot` (ARCHITECTURE section 3.4).
  - 2026-10-21: Done (`usim::sim::Simulation`). States carry `StateQuality` (model, approximate); body-fixed states include the frame's rotation. `fill_snapshot` gives every catalog body relative to a center (the camera's focus), in render units and host axes, with orientation, radius and flags (approximate, no position, no orientation, no radius). The clock starts at J2000 clamped into the common coverage; the core reads no wall clock. Measured: about 6 µs per frame (clock advance and full snapshot of 21 bodies, release), no allocations.
- [x] **P4-03** Body orientation from IAU rotation models (pole right ascension and declination, prime meridian, including the periodic terms in `pck00011.tpc`). Validate with VAL-04.
  - 2026-10-21: Done (`usim::frames::iau_angles`, `iau_transform`; `FrameSystem` now resolves body-fixed frames from rotation elements, `not_found` without them). Rates are analytic. VAL-04: 341 orientations of 11 bodies agree with `sxform` within 2.6e-10 rad, rates within 2.6e-10 of their size (threshold 1 µrad).
- [x] **P4-04** `OrbitPathSampler`: adaptive sampling (denser near periapsis) of a body's path relative to its primary over one orbital period or a given window. Cache results and invalidate them on large time jumps.
  - 2026-10-21: Done (`sample_path`, `sample_revolution`, `OrbitPathCache` in `usim::sim`). Deviation: the revolution is found from the ephemeris (the angle swept around the center) instead of orbital elements, so it needs no GM and works for any center; intervals split until consecutive points are within 0.5 degrees and the chord error within 1e-4. Orbits are drawn around `default_orbit_center` (the Sun, or a moon's planet) rather than the catalog primary, which for a planet is its barycenter. On the default pack every body has a full revolution; the Earth-Moon barycenter's matches its two-body period from the pack's GM values within 1.4e-5.
- [x] **P4-05** `ViewOrigin` and scale modes (`TrueScale`, `EnlargedBodies`): positions re-centered on the view origin, scaled to render units and mapped to the host axis convention, in double precision.
  - 2026-10-21: Done as `usim::presentation::View` (origin, kilometers per render unit, axes, scale mode, body scale) with `to_render_position`, `to_render_radius` and friends. Snapshots take positions relative to a center body first, so the origin is a small offset near the camera.
- [x] **P4-06** C ABI expansion (ARCHITECTURE section 3.5):
  - simulation handle lifecycle, status codes, `usim_last_error_message`
  - catalog queries, state, orientation, clock, snapshot, orbit paths
  - struct size queries for binding validation
  - C ABI tests in `core/tests/`
  - increment `USIM_ABI_VERSION` as needed
  - 2026-10-21: Done: ABI version 2 with 35 functions (catalog with per-value provenance, time parsing and formatting, clock, states, orientations, snapshots, cached orbit paths as render positions, `usim_struct_size`). Every function catches exceptions; errors carry a thread-local message. Tests compare each function with the C++ facade, on the synthetic and the default pack, and pass under `asan`. The Unity binding has the raw declarations and struct mirrors (`NativeTypes.cs`, `NativeMethods.cs`, `ExpectedAbiVersion` 2) and EditMode tests for the struct sizes and a snapshot; the `SafeHandle` and the high-level wrapper remain P5-01.
- [x] **P4-07** CLI commands `catalog`, `state`, `orientation`, `snapshot` and `orbit`, with JSON and CSV output.
  - 2026-10-21: Done, plus `coverage`. Bodies by NAIF ID or catalog name; `--time` takes ISO 8601 with a scale; the pack is found through `--pack`, `USIM_PACK` or the nearest `data/packs/`. The commands live in a library (`usim::cli`) tested in-process (`usim_cli_tests`). A run over the default pack gave a full snapshot and an orbit for every body except the root; that run also found and fixed a march step that rounded past the end of the coverage.
- [x] **P4-08** Write `docs/API.md`: a guide for host authors (the frame loop, the precision strategy, the axis conventions, error handling, thread safety, binding a new language).
  - 2026-10-21: Done; also covers time, data quality flags and provenance, and the CLI as a reference.

**Acceptance criteria**
- The CLI produces a full snapshot and orbit paths for all v1 bodies.
- VAL-04 passes.
- The C ABI tests pass under the `asan` preset.
- No engine is involved anywhere in Phases 1 to 4.

Met on 2026-10-21: `usim snapshot` returns all 21 bodies and `usim orbit` a full revolution for each of the 20 that orbit something (the solar system barycenter is the root). VAL-04 passes (2.6e-10 rad). 172 tests pass under `dev` and `asan`, 174 under `release` with the benchmarks; 8 Unity EditMode tests pass. The core, the C ABI and the CLI build and run without Unity.

---

## Phase 5: Unity MVP (completes v1)

**Goal:** an interactive Solar System viewer driven entirely by the core.

- [x] **P5-01** C# bindings for the full C ABI (`Runtime/Native/`): blittable structs mirroring the C structs, size checks against the ABI's struct size queries at startup, and a `SafeHandle` for the simulation handle.
  - 2026-10-21: Done: `NativeTypes`, `NativeMethods`, `CoreSimulation` (a `SafeHandle`; failures become `CoreException` with the core's message; per-frame calls allocate no managed memory, checked with `Is.Not.AllocatingGCMemory`). `CoreStartupCheck` compares every struct size with `usim_struct_size` at startup.
- [x] **P5-02** `SimulationHost` MonoBehaviour: loads the pack from `StreamingAssets`, owns the simulation handle, advances the clock and fills the snapshot each frame into a reused buffer (no per-frame managed allocations). Add the `copy-pack-to-unity` script.
  - 2026-10-21: Done. Loads in `Start` (after every `Awake`, so all `Loaded` listeners exist); in the Editor it also finds the pack in `data/packs`. Starts at the current UTC time. `tools/scripts/copy-pack-to-unity.sh` copies the pack for builds.
- [x] **P5-03** `BodyView` prefab and spawning from the catalog: a sphere scaled by radius (triaxial where the data provides it) and oriented from the snapshot.
  - 2026-10-21: Done. A 128 x 64 unit sphere scaled by the snapshot's `render_radii` (new in the C ABI: the triaxial radii in host axes, so the host never swaps axes), oriented from the snapshot. Barycenters have no mesh.
- [x] **P5-04** Floating origin through the view origin. Test for jitter at Earth surface zoom and at Pluto distance.
  - 2026-10-21: Done. The camera stays at the origin; its offset from the focus is kept in double precision and passed as the view origin, with one render unit equal to the altitude. PlayMode test: from 1 km above the Earth and near Pluto, directions stay within 1e-5 rad of a smooth path (a pixel is about 8e-4 rad). Minimum altitude 1 km.
- [x] **P5-05** Depth strategy across scales (reversed-Z, dynamic clip planes or layered cameras). Document the choice in an ADR.
  - 2026-10-21: Done: ADR-0009. The core compresses distances beyond a horizon along the line of sight (image unchanged, coordinates small) and gives each body its light direction; one camera with fixed planes; reversed-Z where Unity uses it.
- [x] **P5-06** Camera rig with Input System actions: focus and orbit a body, logarithmic zoom, smooth focus transitions across scales, and free-fly.
  - 2026-10-21: Done (`CameraRig`, `UniverseSimControls.inputactions`): drag to orbit, logarithmic zoom, click to select, double-click or Enter to focus, comma and period to step, F for free fly; 1.5 s transitions on a log-distance path. Tested through its API (batch mode has no input devices); hands-on check by the project owner pending.
- [x] **P5-07** Sun lighting at any scale: lit shader or per-body lighting driven by the Sun direction from the core. Correct phases for the Moon and planets.
  - 2026-10-21: Done. `BodyLit` shader lit from the snapshot's per-body `light_direction` (from true positions), `Star` emissive. Screenshots show phases consistent with the geometry (for example a thin Earth crescent at about 150 degrees phase angle).
- [x] **P5-08** Time controls UI (UI Toolkit): play and pause, signed rate presets, date and time entry with the time scale shown, a "now" button, and a coverage indicator.
  - 2026-10-21: Done (`HudController`, UI Toolkit): pause, reverse, slower and faster through presets from real time to one Julian year per second, date entry with scale (UTC by default), Now, the time in UTC (TT before 1972) and TDB, the data span and a warning at its ends.
- [x] **P5-09** Body info panel: values with units, uncertainty when available, and source citations. Display fallbacks are visibly marked.
  - 2026-10-21: Done: values with units, uncertainty, source id and locator, derivation, the cited sources, the approximate-position reason, distance and speed relative to the orbit center; missing values read "unknown".
- [x] **P5-10** Orbit lines from the core's orbit paths (camera-relative) and screen-space labels, each with a toggle.
  - 2026-10-21: Done. Orbit lines are meshes with a constant width in pixels (`Line` shader): a world-unit width covered the screen when an orbit passed the camera. Approximate orbits have their own color. Labels follow the bodies and hide behind the focus body.
- [x] **P5-11** Scale mode toggle with a persistent on-screen indicator.
  - 2026-10-21: Done. Toggle with a persistent indicator that always shows the mode in use. Enlarged mode rule (now in the core): the star keeps its true size, and a planet and its moons are capped at 40 % of their distance.
- [ ] **P5-12** **(deferred)** Visual assets: textures from verified sources (DATA_SOURCES.md, visual section), with `ATTRIBUTION.md`.
  - 2026-10-21: Deferred by the project owner: v1 shows untextured, Sun-lit spheres; textures move to Phase 6 (P6-07). Revisit with P6-07.
- [x] **P5-13** Unity tests: EditMode tests for snapshot-to-transform mapping, and a PlayMode smoke test that compares a body's position with CLI output for the same time.
  - 2026-10-21: Done: 13 EditMode and 5 PlayMode tests. The PlayMode suite compares body positions with `usim state` for the same instant, checks jitter, camera transitions and limits, and saves screenshots of seven views with `USIM_SCREENSHOT_DIR`.
- [x] **P5-14** Decide OQ-07 (Editor hot reload of the native plugin). If yes, implement a development-only loader.
  - 2026-10-21: The project owner chose no hot reload: the plain `[DllImport]` binding stays, and the Editor is restarted after each core rebuild (as documented in CLAUDE.md).

**Acceptance criteria**
- Opening the main scene shows the Solar System at the current date and time.
- UC-01 to UC-06 work.
- Positions in Unity match the CLI for the same instant, after the documented axis mapping and scaling.
- There are no visible precision artifacts at any supported zoom.

Met on 2026-10-21 in automated checks: the scene loads the pack and shows every body at the current time; the time controls, focus and zoom, info panel, orbit lines, labels and scale modes work through their code paths; positions match `usim state`; jitter tests pass at 1 km above the Earth and near Pluto, and screenshots of seven views show no precision artifacts. Pending: a hands-on check of mouse and keyboard input in the Editor by the project owner (batch mode has no input devices).

---

## Phase 6: Moons and visual fidelity

- [x] **P6-01** Select satellite SPKs for the major moons using NAIF `aa_summaries.txt`, pin them in the lock file, and trim them to the pack window within the size budget. The moons: Phobos, Deimos, the Galilean moons, the major Saturnian moons, the major Uranian moons, Triton and Charon. Take the planet-center segments from the same kernels and remove the barycenter placements of ADR-0008 (`"position": "primary"` in the body selection); revisit NFR-07, since the centers alone add about 165 MB over the de440s window.
  - 2026-10-21: Candidates from `aa_summaries.txt` and the kernels' `.cmt` files (one file per system covering the de440s window with the center and the major moons): `mar099`, `jup365`, `sat441`, `plu060` (fit with DE440), `nep097` (DE441), and for Uranus either `ura184_part-3` (369 MB, fit with DE442, which updated the Uranus barycenter) or `ura111xl-701` to `705` plus `-799` (about 6.7 GB, DE441). Not yet decided or registered. `tools/scripts/download-phase6-kernels.sh` downloads all of them, plus the P6-05 and P6-06 PCKs, into `data/raw/naif/` (resumable, MD5-checked against NAIF); `fetch` now pins such local copies without downloading them again.
  - 2026-10-22: Done ([ADR-0010](adr/0010-moons-in-an-optional-pack.md)). The six kernels are registered as optional files of S-NAIF-SAT and pinned (publisher MD5 verified); Uranus uses `ura184_part-3` (the `ura111xl-705` download was incomplete, and URA184 is the newer solution: up to 1299 km from URA111). Deviation: the moons go into an optional pack, `solar-system-moons-1.0.0.uspack` (2000 to 2100, 304 MB, built locally, not committed; window chosen by the project owner), because even refitted records cannot fit NFR-07; the default pack keeps the ADR-0008 placements. Only segments relative to each system barycenter are read, and only their records in the window. VAL-01 on the moons pack: 3125 states of 71 pairs within 2.0e-6 km of CSPICE.
- [x] **P6-02** Inspect the segment types of the selected kernels. Implement any SPK types beyond 2 and 3 that they need, in both the pipeline extraction and the C++ evaluator.
  - 2026-10-22: Done: every segment of the six kernels is type 2 in J2000; nothing to implement.
- [x] **P6-03** Rotation models for the moons (IAU, including synchronous rotation where the report defines it).
  - 2026-10-22: Done: the 21 moons' IAU models come from `pck00011.tpc` through the existing reader (synchronous rotation is in the kernel's PM terms). VAL-04 on the moons pack: 620 orientations of 20 moons within 1.3e-10 rad of `sxform` (the Moon is in the default pack's VAL-04).
- [x] **P6-04** Saturn's rings and other ring systems: dimensions from a Tier 1 source, rendered as visual-only geometry.
  - 2026-10-22: Done for Saturn ([ADR-0011](adr/0011-sky-background-and-rings.md)): radii of the D, C, B and A rings, the Cassini Division and 13 gaps from the IAU WGPSN Gazetteer (Tier 1), optical depths from the PDS Ring-Moon Systems Node (Tier 2, labeled), transcribed in `data/catalog/rings.json`; pack format 1.2 (section RING), C ABI 3. Unity draws them with opacity from the slant optical depth, the planet's shadow on the rings and the rings' shadow on the planet. Other ring systems are left out until their optical depths are sourced.
- [ ] **P6-05** Optional: high-precision lunar orientation from `moon_pa_de440_200625.bpc` (binary PCK).
  - 2026-10-22: The file is downloaded into `data/raw/naif/` (size checked; the `pck` directory has no checksum list) but not registered; it needs a binary PCK reader in the pipeline and the core.
- [ ] **P6-06** Optional: high-precision Earth orientation from `earth_latest_high_prec.bpc`.
  - 2026-10-22: Downloaded like P6-05, not registered.
- [ ] **P6-07** Textures for the moons with attribution.
- [x] **P6-08** Sky background from a star catalog (SPEC FR-REN-09), requested by the project owner on 2026-10-22.
  - 2026-10-22: Done ([ADR-0011](adr/0011-sky-background-and-rings.md)): the 9096 stars of the Bright Star Catalogue (CDS V/50, S-CDS-BSC5) in pack format 1.2 (section STAR); the core gives directions with proper motion in the display frame and host axes (`usim_fill_stars`, allocation-free); Unity draws them as points sized and colored by a display mapping of V and B-V, with a Stars toggle. The default pack is now `solar-system-1.2.0.uspack` (33.4 MB).

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
