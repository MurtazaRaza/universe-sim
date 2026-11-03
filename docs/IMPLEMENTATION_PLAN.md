# Implementation Plan

| | |
|---|---|
| Status | Active |
| Last updated | 2026-11-02 |
| Current phase | Phase 6 done on 2026-10-26 (moons, rings, the starry sky, the Moon's and the Earth's high-precision orientation, surface maps); next: Phase 7 (small bodies), with Track A (art direction) alongside: A-01 to A-10 and A-13 done (A-04, A-07 to A-10 and A-13 on 2026-11-02); A-04 done; A-12 implemented, waiting for the owner's hands-on check; next A-16, which needs a source for the oceans. Track B (spaceship): B-01 to B-04 and B-06 done on 2026-11-02; the owner's hands-on check (B-05) waits for the end of the current work. Phase 5 done except the owner's hands-on input check. Windows supported since 2026-11-02 (P0-13), and since the same day the only development machine (the Mac is no longer used). The project owner does the hands-on checks (B-05, A-12, Phase 5) together once the current feature work is done. |
| Related | [SPEC.md](SPEC.md), [ARCHITECTURE.md](ARCHITECTURE.md), [DATA_SOURCES.md](DATA_SOURCES.md), [adr/](adr/) |

## How to use this plan

- Work proceeds phase by phase. A phase is done when all its tasks are checked **and** its acceptance criteria are met.
- Tasks have stable IDs (`P<phase>-<nn>`; `A-<nn>` for the art direction track and `X-<nn>` for cross-cutting tasks). Reference them in commits and notes. Do not renumber; append new tasks at the end of a phase or track.
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
| Track A | Art direction (alongside Phases 7 to 9) | Stylized and realistic styles, effects, artist's impressions, interaction feel |
| Track B | Spaceship (stylized style) | A fictional craft to travel between bodies and hover over surfaces, with the core's trip estimates and surface values |

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
- [x] **P0-13** **(deferred)** Windows plugin: a Windows x64 build of `universesim.dll`, a Windows branch in `NativePluginImportSettings`, and the build script extended to produce it.
  - 2026-10-20: Deferred; development happens on macOS, and Windows is needed only to run the viewer on Windows. No Windows machine is required to do this work: the DLL can be cross-compiled on macOS (for example with mingw-w64 from Homebrew, or clang-cl with the Windows SDK), and its exports can be checked there. Only the final check, Unity on Windows loading the DLL, needs Windows. Revisit when a Windows build is wanted.
  - 2026-11-02: Done on Windows 11 with MSVC 19.50 (Visual Studio 2026), requested by the project owner. First MSVC build: fixed `const auto*` on a `std::array` iterator (`iso8601.cpp`), constant `1.0 / 0.0` in tests, unreachable code after `FAIL` (C4702), and `std::getenv` deprecation (C4996: `_CRT_SECURE_NO_WARNINGS` for MSVC). CMake rejects MSVC before 19.30, which contracts multiply-adds under `/fp:precise`. `Result<void>` now holds `std::optional<Error>`: its empty string allocated under MSVC's debug iterators, one allocation per frame. Tests copy the DLL beside them (no rpath on Windows). New: preset `unity-windows`, `enter-vs-dev-shell.ps1`, `build-core-for-unity.ps1` (the `.sh` calls it from Git Bash; checks for x64), the `.dll` import branch with its EditMode test, and `usim.exe` in the PlayMode CLI lookup. The DLL depends only on `KERNEL32.dll`. Results: `ctest` dev 199 and release 201 pass, with the two moons-pack tests skipped (no moons pack on this machine); snapshot 15.1 us (budget 100). Unity 6000.5.1f1 EditMode 26 pass, 1 skipped (macOS plugin). PlayMode 24 pass, 3 skipped (moons pack, mockup harness), positions matching `usim.exe`. Not done: `asan` (not available with MSVC) and the data pipeline on Windows.
  - 2026-11-02: The data pipeline runs on Windows. Its environment is made from an existing CPython 3.12.9 (`requires-python` is 3.12 or newer; macOS pins 3.12.8), and `usim-pipeline` finds the Windows layout (`.venv/Scripts`). The default pack rebuilt here is byte for byte the committed one (SHA-256 87d35ad3...), and all 193 pipeline tests pass (3 skipped: optional sources). CDS could not be reached from this network (TCP 80 and 443 time out), so `sources.json` files may now list mirrors: tried only for files the lock already pins, and their copies must match the pins (DATA_SOURCES.md section 2). The Bright Star Catalogue's files have the VizieR mirror at CfA Harvard, which matched the pins byte for byte. Deviation: `test_committed_unit_fixtures_are_current` compared the regenerated CSPICE fixtures as exact text, which only holds on the platform that made them. Measured here against the macOS arm64 fixtures (SpiceyPy 8.2.0), 10 numbers differ: 9 by 1 to 2 ulp of the largest number on their line, and TDB from TT at J2000 is 0.0 against 2.75e-21 s (far below an ulp of its inputs). The test now keeps the text exact and allows 4 ulp of the largest number on each line.
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
- [x] **P5-12** **(deferred)** Visual assets: textures from verified sources (DATA_SOURCES.md, visual section), with `ATTRIBUTION.md`.
  - 2026-10-21: Deferred by the project owner: v1 shows untextured, Sun-lit spheres; textures move to Phase 6 (P6-07). Revisit with P6-07.
  - 2026-10-26: Done with P6-07.
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
- [x] **P6-05** Optional: high-precision lunar orientation from `moon_pa_de440_200625.bpc` (binary PCK).
  - 2026-10-22: The file is downloaded into `data/raw/naif/` (size checked; the `pck` directory has no checksum list) but not registered; it needs a binary PCK reader in the pipeline and the core.
  - 2026-10-22: Done ([ADR-0012](adr/0012-lunar-orientation-from-the-binary-pck.md)). S-NAIF-MOONPA registered with the PCK and the current lunar frame kernel `moon_de440_250416.tf`; the Moon's body-fixed frame is now `MOON_ME_DE440_ME421` (PCK principal axes plus the frame kernel's offset, records copied bit for bit, no IAU fallback). Pack format 1.3 (section ORNT), C ABI 4 (`usim_body_rotation_model`, shown in the info panel); default pack `solar-system-1.3.0.uspack` (36.9 MB), moons pack 1.1.0. VAL-04 for the Moon: 1.3e-12 rad (default pack) and 1.7e-12 rad (moons pack) from CSPICE `sxform`; the IAU model differed from this frame by up to 1.06e-4 rad.
- [x] **P6-06** Optional: high-precision Earth orientation from `earth_latest_high_prec.bpc`.
  - 2026-10-22: Downloaded like P6-05, not registered.
  - 2026-10-23: Done in the moons pack only ([ADR-0013](adr/0013-earth-orientation-in-the-moons-pack.md), chosen by the project owner). Deviation: `earth_latest_high_prec.bpc` changes twice a week under one name and cannot be pinned, so S-NAIF-EARTHPCK pins `earth_1962_260806_2126_combined.bpc` (ITRF93 relative to ECLIPJ2000, 1962 to 2126, a low-accuracy predict after 2026-08-06); no Earth PCK covers the default pack's span, which keeps `IAU_EARTH`. ORNT (format 1.3, unreleased) now allows ECLIPJ2000 segments, models without a frame kernel offset and a microsecond of coverage past the last record. Moons pack 1.2.0 (325 MB). VAL-04 for the Earth: 3.2e-13 rad from CSPICE `sxform`; `IAU_EARTH` differs from ITRF93 by up to 1.5e-3 rad before 2026-08.
- [x] **P6-07** Textures for the moons with attribution.
  - 2026-10-26: Implemented, waiting for its source downloads ([ADR-0014](adr/0014-surface-map-textures.md); scope chosen by the project owner on 2026-10-23: the planets and moons with a mission mosaic, 2048 x 1024). 19 maps from S-USGS-ASTRO, S-NASA-SVS-MOON, S-NASA-EO-BMNG and S-NASA-PHOTOJOURNAL in `data/catalog/textures.json` (Pluto included with Charon; no map for Venus, Saturn, Uranus, Neptune, Titan, Deimos and the Uranian moons). `usim-pipeline textures` (Pillow 12.3.0 added) reprojects each to one layout by exact area averaging from its ISIS label, GeoTIFF tags or transcribed values and writes ATTRIBUTION.md; Unity draws them on a mesh whose UVs come from the core's axes, with the credit in the info panel. Checked with six maps from local copies (Gazetteer features, the Earth seen from the Sun), then removed. Open: the sources are about 4.8 GB, deferred at the project owner's request to `tools/scripts/fetch-texture-sources.sh` on a faster network; then rebuild the main scene, run the tests, check the maps and commit them.
  - 2026-10-26: Done. The project owner ran the script, and the 64 source files are pinned. Each of the 19 maps lines up with IAU Gazetteer features (see ADR-0014). They take 10.7 MB. Deviation: Ganymede's label rounds its pixel size, which leaves the map 1.3e-4 of a pixel short of 360 degrees, below the 1e-6 degree check. The check is now a thousandth of a pixel, and the seam stays continuous; a test covers both. The default pack's lock-file hash was updated (pack 1.3.0, same content).
- [x] **P6-08** Sky background from a star catalog (SPEC FR-REN-09), requested by the project owner on 2026-10-22.
  - 2026-10-22: Done ([ADR-0011](adr/0011-sky-background-and-rings.md)): the 9096 stars of the Bright Star Catalogue (CDS V/50, S-CDS-BSC5) in pack format 1.2 (section STAR); the core gives directions with proper motion in the display frame and host axes (`usim_fill_stars`, allocation-free); Unity draws them as points sized and colored by a display mapping of V and B-V, with a Stars toggle. The default pack is now `solar-system-1.2.0.uspack` (33.4 MB).
- [x] **P6-09** Rendering cost of the viewer (levels of detail, batching and other per-frame costs), requested by the project owner on 2026-10-26.
  - 2026-10-27: Done. Measured by a new PlayMode test (`RenderingCostTests`, 1920 x 1080, moons pack, Editor): main thread 10.3 to 2.2 ms at the start view, 21.3 to 2.3 ms at the Jupiter system, 9.2 to 2.1 ms at the Earth; triangles 546k to 40k at the start view. Most of the time was the C# binding: Unity's Mono copied every element of an `[Out]` array back after each call, 16384 points per orbit line (6 to 14 ms per frame). The snapshot, star and orbit buffers are now pinned and passed as pointers (C ABI unchanged). Bodies draw a sphere of 128 down to 8 segments chosen by their size on screen (outline within 0.5 px, `SphereLods`), with a material per body instead of a property block so they stay in the SRP Batcher; mapped spheres share meshes by radii ratios and use 16-bit indices. The orbit lines are one mesh and one draw call, labels move by `translate` (no layout pass), the template's SSAO feature and the depth and opaque texture copies (read by no shader) are off, and vSync is on.

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

## Track A: Art direction (alongside Phases 7 to 9)

**Goal:** the stylized look of SPEC section 11.1 over unchanged data, with the realistic style one click away ([ADR-0015](adr/0015-stylized-presentation-over-real-data.md)). The track runs alongside the phases and touches only the Unity host and the visual pipeline unless a task says otherwise. A-01 comes first, then A-02 to A-04, which the later tasks build on.

- [x] **A-01** **(user)** Art direction brief (OQ-09): mockups of two or three candidate looks on the Earth, the Moon, Mars, Jupiter and Saturn's rings (palette, number of shading bands, outlines or none, glow strength), for the project owner to choose from. Record the choice in `docs/ART_DIRECTION.md` (palette, shading rules, the effects in priority order, the hue tolerance A-06 checks) and mark OQ-09 decided.
  - 2026-10-27: Done. Three rounds of mockups were rendered from the viewer (`ArtDirectionMockups`, a PlayMode harness run with `USIM_MOCKUP_DIR`, shaders in `Assets/UniverseSim/Mockups/`): soft toon, then comic and painted looks, then three steps toward a polished console game. The project owner chose the middle step, "Adventure": no bands or outlines, a brighter day side, satin sheen, rims on both sides, thick atmospheres, sparkling stars and bloom. Recorded with reference renders in `docs/ART_DIRECTION.md`; hue tolerance 5 degrees (measured at most 3.4). The mockups stay as the reference until A-05 to A-09 are done.
- [x] **A-02** Visual style switch (FR-REN-12): a host-wide style setting (stylized by default, or realistic) that materials, shaders and effects read (a global shader keyword or property, and an event for components); a HUD toggle and a persistent style indicator beside the scale mode indicator; the choice remembered between sessions. Until the effects exist, both styles draw the current look. Tests: switching style changes no transform, snapshot value or HUD readout; the scene starts in the stylized style; the PlayMode suites (positions against the CLI, jitter, lighting, camera) run in both styles, and `USIM_SCREENSHOT_DIR` saves the views in both.
  - 2026-10-27: Done. `VisualStyleSettings` on the `UniverseSim` object sets the global shader keyword `USIM_STYLE_STYLIZED`, raises `Changed` and remembers the style in `PlayerPrefs`; the HUD has a "Realistic style" toggle and a "Stylized style" or "Realistic style" indicator beside the scale indicator. `VisualStyleTests` checks the default, the toggle, the remembered choice, and that switching leaves the snapshot and request bytes, body transforms, light directions, camera and every HUD readout identical. `MainSceneTests` runs in both styles (screenshots in `stylized/` and `realistic/`); the light directions stand in for "lighting" until A-05's terminator test. The tests keep and restore the Editor's remembered style. EditMode 26/26, PlayMode 20 passed (the mockup harness skipped).
- [x] **A-03** Exaggeration register and the "what is exaggerated" panel (FR-REN-12, FR-REN-13): each effect component declares what it exaggerates, by how much, and whether it is active in the current view; a HUD panel lists the active entries, and the list is empty in the realistic style. A test fails if an effect component is not registered.
  - 2026-10-29: Done. Effects implement `IStyleEffect` (their `Exaggeration` entries and `ActiveInView`) and are listed in `ExaggerationRegister` by the scene builder; the HUD's "What is exaggerated? (n)" panel (top left, expands on click) lists the active entries. `ExaggerationTests`: every `IStyleEffect` in the scene is registered, every effect type is in the scene, every component that reads the style is an effect, the panel is empty in the realistic style, and with every registered effect disabled the stylized style draws the realistic view (measured at most 2 of 255 levels apart), so no stylization can show without being listed. The stylized shaders read global properties that the effects set and that are neutral at zero.
- [x] **A-04** Attribution for artistic assets (ADR-0015 decision 6): extend `data/catalog/textures.json` (or a sibling catalog) and `usim-pipeline textures` so effect textures and artist's impressions appear in the generated `ATTRIBUTION.md` with their license, and an artist's impression also with its references. Pipeline tests, and the EditMode attribution test covers the new entries. Done before any artistic asset is committed.
  - 2026-11-02: Done, as a sibling catalog: `data/catalog/artistic-assets.json` lists each artistic asset (a file under `Assets/UniverseSim/Art/`) with its kind (effect texture, artist's impression, icon, UI art, model), title, author, origin (original or licensed) and license; an artist's impression also needs its body and at least one reference with its credit and URL. `usim-pipeline attribution` checks the catalog against the Art folder (every listed file exists, every file there is listed) and rewrites the last section of `ATTRIBUTION.md`, so adding an asset needs none of the surface maps' sources; `usim-pipeline textures` writes the same section. The list is empty: every effect, icon, cursor and model so far is drawn by code. Pipeline tests (`test_artistic.py`) and the EditMode `ArtisticAttributionTests` (every file under `Art/` is in `ATTRIBUTION.md`). With the texture sources now on this machine, `test_committed_textures_are_current` rebuilt every surface map and `ATTRIBUTION.md` byte for byte.
- [x] **A-05** Stylized body shading in `BodyLit` (FR-REN-07): banded diffuse light, rim light, a lifted night side and optional soft outlines, as chosen in A-01; the realistic style keeps the Lambert shading. The terminator stays where the core's light direction puts it: a PlayMode test renders the Moon at a known phase in both styles and finds the terminator in the same place within a pixel. The ring shadows keep working.
  - 2026-10-27 (from A-01): no bands and no outlines. Instead a brighter smooth day curve with a shade-to-light tint, a satin sheen, a day rim in the surface's color, a cool night rim and a brightness cap below the bloom threshold (ART_DIRECTION.md section 3). The sun glint waits for the ocean property (A-07).
  - 2026-10-29: Done, done before A-04 at the project owner's request (2026-10-27) since shader effects add no artistic assets. `BodyLit` has a stylized variant (keyword `USIM_STYLE_STYLIZED`) driven by `StylizedBodyShading`. `BodyShadingTests`: the white Moon at a 60 degree phase has its terminator within one pixel of where the core's light direction puts it in each style, and the two styles within one pixel of each other, on all 86 scan lines. With post-processing off, no stylized surface exceeds the cap of 0.95. Ring shadows are unchanged (same transmission term).
- [x] **A-06** Color treatment of the real surface maps (FR-REN-11, SPEC section 11.1): grading, saturation and optional posterization in the shader in the stylized style, and the maps as published in the realistic style. A test checks that each map's average hue stays within the tolerance set in A-01.
  - 2026-10-27 (from A-01): a Kuwahara smoothing, saturation x1.6 and a luminance contrast of 0.9 that lifts shadows; no posterization. Tolerance: 5 degrees for maps with a mean chroma of 0.02 or more, and grey maps stay grey (ART_DIRECTION.md section 4).
  - 2026-10-29: Done in `Shaders/SurfaceMapTreatment.hlsl`, driven by `SurfaceMapTreatment`; applied only to mission maps (`_MapTreatment`, set by `BodyView`). The smoothing reads one mip coarser than the screen needs and never finer than mip 1, which reproduces the mockups at their distance and keeps the patches about 3 pixels wide farther out. `SurfaceMapTreatmentTests` draws every map through the same shader code and checks the hue tolerance: the largest change is 3.5 degrees (Io), as measured in A-01, and the monochrome maps stay exactly grey.
- [x] **A-07** Atmosphere halos (FR-REN-13, ADR-0015 decision 4): a limb glow on bodies that have an atmosphere, brightest on the sunlit side and following the core's light direction. Which bodies have one comes from a registered source through the pipeline, never typed into host code; decide in an ADR whether it travels in a visual catalog beside `textures.json` or in the pack and the C ABI. The halo's thickness and intensity are registered exaggerations.
  - 2026-10-27 (from A-01): the chosen look adds twilight colors at the limb (the Earth's red, Mars's blue) and a sun glint on oceans. Both imply physical properties, so the same ADR and sourcing cover them (ART_DIRECTION.md section 5).
  - 2026-11-02: Done with B-06 ([ADR-0018](adr/0018-atmospheres-and-sky-cues.md)). Which bodies have an atmosphere comes from the pack's surface conditions through the core (C ABI 7, `usim_body_atmosphere`: a positive surface pressure or a reference pressure level), so no new pack data: Venus, the Earth, Mars, the four giants and Pluto; Mercury and the Moon have 0 bar in the source's table. The twilight hues are sourced cues in a visual catalog, `data/catalog/sky-cues.json` (`usim-pipeline sky-cues` writes `Data/SkyCues.json`): the Earth's red and yellow (NASA Earth Observatory, ISS070-E-76065) and Mars's blue (NASA/JPL PIA00917), each with the source's sentence. `StylizedAtmospheres` draws the halo (`AtmosphereHalo.shader`, ported from the A-01 mockup) with the A-01 values and per-body display choices. `AtmosphereTests`: halos exactly on the bodies the core reports, a twilight tint only with a sourced hue, none in the realistic style; on the Earth at right angles to the light the halo adds 17,106 (sum of linear luminance) on the sunlit side, 0.0 on the night side and at most 0.0000 beyond 1.3 radii. Deviation: the ocean glint has no source for where the oceans are, so it moved to A-16.
- [x] **A-08** The Sun: corona glow, glare and bloom (URP post-processing). The Sun's disk keeps its true size in each scale mode, and the bloom is tuned so phases and crescents stay readable. Registered as exaggerations; off in the realistic style.
  - 2026-10-27 (from A-01): the stylized camera also gets color adjustments, a vignette and the indigo sky. The realistic style turns off the bloom and vignette the scene still carries from the URP template (ART_DIRECTION.md section 7).
  - 2026-10-29: Camera part done (with A-05): `StylizedCamera` adds a runtime volume (bloom, color adjustments, vignette) at weight 1 in the stylized style and the indigo sky; the scene builder turns off the template's bloom and vignette, so the realistic camera is Neutral tonemapping only. Still open: the Sun's corona and glare, and the check that phases and crescents stay readable under the bloom.
  - 2026-11-02: Done (on Windows). `StylizedSun` makes the disk a warm color at 4x (the stylized `Star` variant), so it blooms. `SunGlow` adds a corona out to 4 solar radii and a halo and six rays whose lengths grow with the disk on screen (rays 12 to 90 px, so the overview is not swamped). The glow is depth-tested at the Sun's center, so nothing covers the disk, and the glare scales with the part of the disk no body covers. `StylizedSunTests`: the disk measures 163.0 px in both styles against 163.1 projected (9.0 against 9.7 in the enlarged mode, where the rig keeps 100 radii away), and the glow beside it reaches at most 2.30 against the disk's 3.45. `BodyShadingTests` adds a crescent Moon at a phase angle of 150 degrees with the Sun in view and all post-processing on. The terminator stays within 1 px on all 86 scan lines, and turning only the bloom off changes neither the crescent (0.238) nor the night side (0.069) at three decimals. The crescent-to-night ratio of 3.45 (realistic 5.7) comes from A-05's lifted night side, so the bound is 3. The bloom kept its A-01 values.
- [x] **A-09** Stylized stars and rings (FR-REN-09, FR-REN-10): a brighter, softer star mapping and a color and contrast treatment for the rings. Both mappings stay monotonic, checked by tests: a brighter star never looks fainter than a dimmer one, a bluer star never looks redder, and a denser ring region never looks thinner. The realistic style keeps the ADR-0011 mappings.
  - 2026-10-27 (from A-01): larger stars with four-point sparkles on the brightest, and a sparse-to-dense ring palette (ART_DIRECTION.md section 6).
  - 2026-11-02: Done. `StylizedStars` and `StylizedRings` drive stylized variants of `StarField` and `Rings` with the A-01 values. `StarAndRingOrderTests` draws synthetic stars and ring bands through the viewer's shaders, before post-processing, in both styles. From V 6.5 to -1.5 the light per star rises from 0.66 to 37.7 (realistic) and from 2.8 to 403 (stylized), the red-to-blue ratio rises with B-V, and ring light and opacity rise with the optical depth, face on and at 60 degrees. `StarField.DisplayOf` and `WriteQuad` hold the display mapping, so the test draws stars exactly as the viewer does.
- [x] **A-10** Orbit lines and labels (FR-REN-06, FR-REN-13): bolder lines colored by body kind, a fade along each path that shows the direction of motion, lines that brighten near the focused body, and icons on the labels. Every line still follows the core's path, and approximate orbits stay distinguishable.
  - 2026-11-02: Done. `StylizedOrbitLines` drives a stylized variant of `Line` (2.6 px, colors by kind, approximate lines amber and dashed). Each line fades back in time from its body, using the core's time-ordered path and the path point nearest to the drawn body. The focused body's orbit and the orbits around it are brighter. `StylizedLabels` shows a `BodyIcon` for the kind of body, drawn by code, so it is no artistic asset. `OrbitLineStyleTests` checks that the vertices are the same in both styles and are the core's path points, that the Earth three days earlier sits at an earlier point of the same path, that the focus highlight follows the focus, that approximate lines get their category exactly when the core flags them, and that the icons match the kind of body and its line color and show only in the stylized style. The new effects derive from a small `StyleEffect` base class; the older ones are unchanged.
- [ ] **A-11** **(user)** Artist's impressions (FR-REN-14) for the bodies without a verified map (Venus, Saturn, Uranus, Neptune, Titan, Deimos, the Uranian moons). The project owner chooses the bodies, the method (procedural shaders, painted maps or both) and, for painted maps, who paints them. Each impression lists its reference images or descriptions through A-04, adds no unobserved features, shows the "artist's impression" label and its references in the info panel, and appears only in the stylized style (the realistic style keeps the plain sphere). Tests: the label appears, and the realistic style draws no impression.
- [ ] **A-12** Interaction feel (FR-REN-15): eased camera flights (refining the P5-06 transitions), hover highlight and cursor feedback on bodies and labels, selection feedback, and UI Toolkit transitions for panels and buttons. Animation never moves a body or the clock. A hands-on check by the project owner closes the task, together with the Phase 5 input check.
  - 2026-11-02: Implemented; open until the owner's hands-on check (with B-05 and the Phase 5 input check, which the owner does once the current work is done). Flights between bodies follow smootherstep on a log scale of distance, last 0.9 s plus 0.45 s per decade (at most 3 s) and turn the view by an eased slerp instead of at once; zoom steps ease with a time constant of 0.08 s; the hovered body (under the pointer or its label) gets a thin ring, a brighter label and a pick cursor drawn by code (`PickCursor`), and selects nothing; the selected body gets a gold ring that pulses in, the info panel slides in, a label click selects and a double-click flies; buttons and toggles animate in USS. Same in both styles (ART_DIRECTION.md section 12). `InteractionTests`: over a 1.69 s flight to the Moon in 586 frames the log distance followed smootherstep within 1e-3 and the view turned 0.00 of 73.7 degrees in its first frame, within 0.135 degrees of the eased slerp throughout, with the clock and the Moon's position unchanged; zoom settles on its target; hovering a label selects nothing and the rings sit on their bodies within a pixel. `MainSceneTests` waits for the flight to end instead of a fixed 1.8 s.
- [x] **A-13** Rendering cost with every effect on (NFR-11): extend `RenderingCostTests` to the stylized style, meet 60 frames per second at 1920 x 1080 on the development machine (Windows since 2026-11-02, SPEC NFR-11) in a player build, and add quality tiers that scale effects down before the frame rate drops. Record the measurements here.
  - 2026-11-02: Done. `EffectsQuality` picks an effects tier, automatically from the smoothed frame time (`AdaptiveTier`: down after 1.5 s over 1/60 s, up after 8 s under 60 % of it, 2 s of settling) or fixed and remembered; the medium tier lightens the bloom and drops the map smoothing, the low tier also drops the bloom and the star sparkles; halos and the sky stay. The style indicator and the exaggeration panel show the tier's effect; batch runs keep the high tier. `PlayerBuilder` builds a Windows player (`UniverseSim/Builds/Windows/`) and `PerformanceBenchmark` (`-usim-benchmark <file>`) measures it. At 1920 x 1080, vertical sync off, moons pack, Direct3D 12 on the i7-13700H and RTX 4060 Laptop: every view with every effect on above 420 frames per second (the slowest, Saturn at 3 radii: mean 2.36 ms, 99th percentile 3.52 ms; realistic 2.30 ms), landed on Mars 1.82 ms, the effects adding 0.05 to 0.16 ms; the automatic tier stays high (table in ART_DIRECTION.md section 13). `RenderingCostTests` logs the counters for the realistic style and both stylized tiers (SetPass calls at the Earth: realistic 8, stylized high 26, low 10); `EffectsTierTests` (EditMode) checks the automatic rule and `EffectsQualityTests` the tiers in the scene, with no change to the snapshot, positions or values.
- [ ] **A-14** Comet tails (after P7-01 and P7-06): a stylized tail on each comet, pointing away from the Sun along the direction the core gives, with its length a registered exaggeration.
- [x] **A-15** **(user)** Decide OQ-10 (guided experiences: tours, upcoming eclipses, transits and conjunctions, challenges) and OQ-11 (sound), and break the chosen ones down into tasks here. Events are found by the core from the ephemeris and validated (for example against the CSPICE geometry finder), never computed in the host.
  - 2026-11-02: Decided by the project owner after the first hands-on check found the viewer not yet fun (B-05): a first pass of all four options offered (a ship-first start with better flight, facts in human terms, an explorer's logbook, time as a toy), music with engine and interface sounds, and guided tours later; upcoming sky events and a photo mode not chosen ([ADR-0019](adr/0019-play-start-facts-logbook-time-and-sound.md)). Broken down into A-17 to A-23.
- [ ] **A-16** Sun glint on oceans (split from A-07, ADR-0018 decision 5): register a source for where a body's oceans are (a land and water mask matching the Earth's surface map), bring it through the pipeline beside the surface maps, and add the glint of ART_DIRECTION.md section 3 only there and only in the stylized style. Tests: the glint appears only where the mask says water, and not in the realistic style.
  - 2026-11-02: No source yet. NASA's Blue Marble collection (S-NASA-EO-BMNG) publishes no land and water mask, and the MODIS land and water mask (MOD44W) needs an Earthdata login, which the pipeline does not use. Open until a downloadable Tier 1 mask is found.
- [x] **A-17** Ship-first start and flight feel (ADR-0019 decisions 1 and 2): start near the Earth with the Moon in view, in the ship in the stylized style (`StartExperience`); mouse steering without a button held; speed streaks and a wider view at speed, registered as exaggerated (`SpeedCues`, `SpeedStreaks.shader`, `ShipPace`); an arrival banner with a fact. Tests: the start places the camera near the Earth with the Moon on screen and the ship out, and tests keep the system view; the cues stay off at cruise and in the realistic style and come on while boosting and mid-trip, and are listed while on.
  - 2026-11-03: Done. `StartExperience`, mouse steering in `ShipPilot` (dead zone 12 %, full turn at 85 %, 60 degrees per second; off over the HUD, while dragging or typing, and in tests) with its reticle in `ShipHud`, `ShipPace`, `SpeedCues` with `SpeedStreaks.shader` (listed as exaggerated), and the arrival banner in `LogbookHud`. `PlayTests` check the start (the Earth and the Moon on screen, the ship out, the Moon selected; tests keep the system view through `PlayModeDefaults`), no cues at rest, strong cues and a wider view mid-trip with the entry listed, both gone 2.5 s after arrival and in the realistic style, and the banner naming the Moon. Not tested: the steering itself (batch runs have no mouse).
- [x] **A-18** Facts in human terms (ADR-0019 decision 3): the info panel's "What it's like here" (`BodyFacts`) above a "Data and sources" section with the existing values and citations. Tests: the Earth, the Moon, Mars and Jupiter show the expected facts from the core's values (ratios checked against the core's own site gravity and radii), and the data section keeps every value and source.
  - 2026-11-03: Done. `BodyFacts` and the info panel's foldout. Deviation from the first design: the core's orbital period is always the system's orbit around the Sun (the Moon's is a year), so the panel shows it as the year for every body and the Moon goal (A-20) measures the angle the Moon sweeps instead of a period. `PlayTests` check the Earth's facts, the Moon's weight and width and Jupiter's weight against ratios of the core's own site gravity and radii, and that the data section keeps GM and the sources.
- [x] **A-19** Time controls (ADR-0019 decision 5): the throttle slider (`TimeThrottle`), the timeline over the coverage through the core's instant functions, Tab held to rush time, and the date field behind "Set date". Tests: the throttle's positions map to the presets both ways; dragging the timeline sets the clock; rushing restores the rate on release.
  - 2026-11-03: Done. The Slower, Reverse and Faster buttons gave way to the throttle; [ and ] still step the rate. `PlayLogicTests` check the throttle's mapping both ways; `PlayTests` check its ends and middle, the timeline's middle against the coverage within 1e-6 through the core's instants, and a rush restoring the rate and the pause.
- [x] **A-20** The explorer's logbook (ADR-0019 decision 4): `Logbook` (goals checked from core values, progress in PlayerPrefs), `LogbookHud` (button and J, the panel, toasts). Tests: the saved form round-trips; a goal's condition reached in the scene completes it once, shows a toast and is remembered; goals without their bodies are left out.
  - 2026-11-03: Done. 14 goals on the default pack. `PlayLogicTests` check the saved form; `PlayTests` check a goal reached once with its toast, its saved key, the button's count and the panel's check mark. Not tested by a scene: leaving out goals whose bodies are missing (every body is in the default pack).
- [x] **A-21** Sound (ADR-0019 decision 6): `SoundScape` with the synthesized `AmbientMusic`, `EngineSound` and `UiSounds`; the Sound (M) and Music toggles. Tests: each synthesizer is deterministic, bounded and audible when on and silent when off, and the engine follows its targets; the toggles are remembered.
  - 2026-11-03: Done. `PlayLogicTests` check that the music is seeded (two runs equal), bounded and audible after its intro, that the engine is silent with zero targets and quieter at a lower level, that every interface cue is audible and ends within 4 s, and the soft clip. Not tested: the toggles' memory and the sound in the scene (tests run silent through `PlayModeDefaults`). On Windows: Unity EditMode 54 (1 skip, the macOS plugin) and the full PlayMode suite 64 (1 skip, the mockup harness) pass, with screenshots in a `play` folder reviewed.
- [ ] **A-22** Guided tours (OQ-10, chosen for later): an autopilot tour through highlights with a short card at each stop, every card's values from the core. To be broken down when started.
- [ ] **A-23** **(user)** Hands-on check of A-17 to A-21 by the project owner, with B-05: is it fun?

**Acceptance criteria**
- The viewer opens in the stylized style; one click switches to the realistic style and back, and the active style is always shown (UC-09).
- In both styles, positions, orientations, the terminator and every displayed value are identical (automated tests).
- The "what is exaggerated" panel lists every active exaggeration, and none in the realistic style.
- Every artistic asset is in `ATTRIBUTION.md` with its license, every artist's impression also with its references, and every artist's impression is labeled in the viewer.
- NFR-11 is met with every effect on.
- The project owner approves the look and the feel hands-on.

---

## Track B: Spaceship (stylized style)

**Goal:** in the stylized style, a spaceship that carries the camera from body to body and down over their surfaces, so a learner gets a feel for distances, travel times and what each place is like, with every value from the core (SPEC UC-10, FR-SIM-11, FR-REN-16, [ADR-0016](adr/0016-spaceship-and-exploration-values.md)). Requested by the project owner on 2026-11-02, who chose fast flight with real facts, hovering with real conditions and the sky, and an original model.

- [x] **B-01** Decision and documents: ADR-0016, SPEC (UC-10, FR-SIM-11, FR-REN-16, the non-goal on fictional bodies), ARCHITECTURE, API.
  - 2026-11-02: Done.
- [x] **B-02** Core exploration values: `usim::orbits` (osculating semi-major axis, period, Hohmann transfer), `usim::surface` (reference ellipsoid, planetocentric points and their inverse, local horizon) and `usim/sim/exploration.hpp` (transfer estimate, surface site, day length); C ABI version 5 with C# bindings; CLI commands `transfer`, `site` and `day`.
  - 2026-11-02: Done. Unit tests on synthetic values, the C ABI against the C++ facade, and validation on the default pack. The mean solar day matches the Sun's hour angle followed over whole orbits within 1.2e-6 (Mercury), 4.7e-6 (Venus), 5.2e-8 (Earth), 1.1e-5 (Moon) and 6.4e-10 (Mars); threshold 5e-5. Deviation from the first version: the instantaneous rotation rate gave 4.4e-4 for Mercury and 9.6e-5 for the Moon (their forced librations), so the period uses the IAU mean rate (W1 + 2 W2 d), and only bodies without IAU elements use the instantaneous rate (flagged).
- [x] **B-03** Unity: `ShipPilot` (flight relative to the nearest body, trips of 4 to 14 s, descent to and hovering over a surface site, the camera rig's new `ICameraPilot`), `ShipHud` (toggle and panel), `ShipMesh` and the `Ship` shader, the V, T and L keys, listed in the exaggeration register; EditMode and PlayMode tests (`ShipTests`, with screenshots in a `ship` folder).
  - 2026-11-02: Done. Hovering, not landing: until B-04 the ship never shows itself on a surface whose kind is not in the data. The model is code, so it needs no ATTRIBUTION.md entry (A-04 still applies to any modeled ship). On Windows: `ctest` dev 223 and release 225 pass (2 moons-pack skips); Unity EditMode 31 pass (1 skip, the macOS plugin) and PlayMode 28 pass (3 skips: the moons pack, the mockup harness), including the 4 `ShipTests`. The mesh and the panel were checked in screenshots near the Earth, flying to the Moon and Mars, and hovering over the Moon and Mars.
- [x] **B-04** Sourced surface conditions: which reference surfaces are solid and which are pressure levels (the IAU report, Archinal et al. 2018, gives the giant planets' radii at 1 bar), and mean surface temperature and pressure (Tier 1 where it exists, else S-NSSDC-FACT as a labeled Tier 2 input). Through the pipeline into the pack (a format change) and the C ABI; then the ship touches down on solid surfaces and stops at the cloud tops of the others, and the panel shows the values with their sources. Needs the data pipeline (macOS, or the pipeline set up on Windows).
  - 2026-11-02: Done on Windows ([ADR-0017](adr/0017-surface-conditions.md)). Deviation, chosen by the project owner: the IAU report could not be fetched (its publisher refuses automated downloads), so Jupiter's, Saturn's and Uranus's 1 bar levels come from their NSSDC sheets, labeled Tier 2, and Neptune's from the comment in `pck00011.tpc` (Tier 1). Mean temperatures for the ten bodies of NSSDC's comparison table and surface pressures for its six terrestrial bodies (Tier 2) are transcribed in `data/catalog/manual/surface-conditions.json` as the page's text and units, converted to K and Pa by definition (NIST SP 811), with the conversion recorded. Pack format 1.4 adds section SURF; the default pack is now `solar-system-1.4.0.uspack` (the moons pack becomes 1.3.0 when next built). C ABI 6: the fields and `usim_body_surface_kind`; the CLI's `site` and `catalog` print them. The ship lands on solid ground, resting on the drawn sphere (which lies up to R (1 - cos(pi/128)) inside the ellipsoid; `SphereMesh.DrawnRadiusFraction`, checked against the meshes by ray intersection), stops 2 km above a giant's 1 bar level, and hovers elsewhere. The panel and the info panel show the values with their sources and tiers. Tests: pipeline 207, `ctest` dev 225 and release 227 pass (moons-pack skips); Unity EditMode 34 pass (1 skip, the macOS plugin) and PlayMode 39 pass (3 skips), with `ShipTests` run again after a fix to the facet depth (the exit facet can be in the neighboring grid cell, so the smallest plane distance over the 3 x 3 cells is used; found by the ray-intersection test on the 8-segment sphere).
  - 2026-11-02: The moons pack was rebuilt on Windows, now the only development machine: `solar-system-moons-1.3.0.uspack` (324,818,352 bytes, SHA-256 da11628c55569936a6e7578dd10e87e8727578d513440497fb911a11f076cff6; 41 bodies, 184 quantities with the SURF values). Its validations (VAL-01 and VAL-04 against CSPICE) pass in `ctest` dev and release, and with it in place (the Editor prefers it) Unity EditMode passes 39 (1 skip, the macOS plugin) and PlayMode 44 (1 skip, the mockup harness), including the moon label test the default pack skips.
- [ ] **B-05** **(user)** Hands-on check of the ship by the project owner: flight speeds, the chase camera, trip durations, the hover height and the panel. Together with the A-12 and Phase 5 input checks.
  - 2026-11-02: First finding: the date field kept taking the keyboard. The project-wide input actions bind UI navigation to WASD and the arrows, so the movement keys walked the HUD's focus into the field, which then took every key (and Space or Enter clicked the focused button). Fixed by `HudKeyboard`: only text fields take the focus, by a click; navigation never moves it; Enter with a good date, Escape or a click elsewhere gives it back; the camera rig's hotkeys and key zoom now wait while typing, as the ship's and the HUD's did. `HudKeyboardTests` (PlayMode). The owner also found the viewer not yet fun to use (SPEC P9); the response is in A-15.
  - 2026-11-03: Second finding: the ship did not fit the Adventure look. It was thin flat plates, a hull that bloomed to white, and a copy of the body shading that had drifted (rims on the night side, no brightness cap), and it ignored the sky and the ground around it. Now (ART_DIRECTION.md section 11): `ShipMesh` builds rounded clay-like parts with smooth normals and soft occlusion baked where they meet; the `Ship` shader reads the bodies' own stylized parameters, cap included, so only the nozzles bloom; under a sky its daytime hue lights the ship; a landed ship gets a contact shade (`ShipShadow`, drawn without a depth test between the bodies and the ship, because the drawn facets hid a depth-tested quad); and in flight the view looks down at the ship 8 degrees more, so its top shows. `ShipLookTests`: from 16 views and lights the brightest paint is 0.950 (the cap), the lit nozzles exceed 1.9; in a body's shadow the ship's mean luminance is 0.056 against 0.389 lit, outlined by the night rim (brightest 0.615). `ShipTests.ALandedShipHasAContactShadeUnderIt`: by day on Mars the ground beside the ship has 75 % of the luminance of the ground beyond the shade. Unity EditMode 44 pass (1 skip, the macOS plugin) and PlayMode 55 (3 skips: the mockup harness and the two views saved only with USIM_SCREENSHOT_DIR, which pass when it is set). The flight feel stays part of this check.
- [x] **B-06** The sky at a surface site: an atmosphere's daytime sky and twilight for bodies a registered source shows to have one (with A-07).
  - 2026-11-02: Done with A-07 ([ADR-0018](adr/0018-atmospheres-and-sky-cues.md)). `StylizedSky` (`Sky.shader`) draws the sky over the stars when the camera is within the halo of a body with an atmosphere: the daytime hue from the sky cues (Venus orange, Pieters et al. 1986; the Earth blue, NASA Space Place, labeled Tier 2; Mars brownish gray, NASA/JPL PIA01547), a neutral gray where no source gives one, the twilight hue around a low Sun, and clear at night. Its strength follows log10 of the pressure (full from 1,000 Pa; Pluto's 1 Pa gives none). The ship and info panels name the atmosphere and cite each cue. The stylized rims of `BodyLit` now fade out near the ground, where every view is grazing and they lit the whole night side. `AtmosphereTests`: on Mars by day the median of the sky above the ship is (0.232, 0.151, 0.094) (linear, red over blue), on the Earth (0.047, 0.174, 0.436), the Earth's night sky (0.001, 0.001, 0.003) against the day's under a quarter of its luminance, the Moon has no sky, and the ship panel cites the cues. With 5 decades of pressure Mars's sky had strength 0.6 and let the stars through, so it is full from 1,000 Pa. On Windows: `ctest` dev 225 and release 227 pass (2 moons-pack skips), the pipeline 225 (2 skips), Unity EditMode 39 (1 skip, the macOS plugin) and PlayMode 42 (3 skips: the moons pack, the mockup harness), and `AtmosphereTests` and `ExaggerationTests` again after the scene rebuild.
- [ ] **B-07** **(deferred)** Terrain relief from elevation models (for example LOLA for the Moon and MOLA for Mars) and real transfer trajectories (a Lambert solver over the Phase 7 propagator). Not chosen by the project owner on 2026-11-02; revisit if wanted.

---

## Cross-cutting tasks (any phase)

- [ ] **X-01** Keep `CLAUDE.md`, `README.md` and the documents in `docs/` in sync with the code.
- [ ] **X-02** Every new data source: add it to `DATA_SOURCES.md` and `data/sources.json` before using it.
- [ ] **X-03** **(user)** Choose a license for the project (OQ-05).
