# Architecture

| | |
|---|---|
| Status | Draft v0.5 |
| Last updated | 2026-10-22 |
| Related | [SPEC.md](SPEC.md), [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md), [adr/](adr/) |

This document describes how the system is built. Requirements live in [SPEC.md](SPEC.md). Where this document says "planned", the code does not exist yet. Update this document whenever the implementation diverges from it.

---

## 1. System overview

```
  Verified sources (JPL/NAIF, IAU, IERS, ESA, ...)
            |
            |  fetch over HTTPS, verify checksums, record in data/sources.lock.json
            v
  +-----------------------------+   build   +------------------------------+
  | tools/pipeline (Python)     |---------->| data/packs/*.uspack          |
  | fetch, verify, build, info, |           | versioned binary data pack:  |
  | reference                   |           | ephemeris + catalog +        |
  +-----------------------------+           | provenance                   |
            |                               +------------------------------+
            | reference vectors (CSPICE, Horizons)          |
            v                                               | load
  data/reference/ (committed, small)                        v
            |              +--------------------------------------------------------+
            +------------> | core/  (C++20, standard library only, no engine)       |
             validation    |                                                        |
             tests         |  usim_core (static, namespace usim)                    |
                           |    math | time | frames | catalog | ephemeris |        |
                           |    propagation | simulation | presentation | data      |
                           |                                                        |
                           |  universesim (shared library, stable C ABI: usim.h)    |
                           +--------------------------------------------------------+
                                |                     |                       |
                    C ABI via   |          C++ API    |                       | C ABI or C++ API
                    P/Invoke    v                     v                       v
                 +----------------------+  +-------------------+  +---------------------------+
                 | Unity host           |  | usim CLI host     |  | Future hosts              |
                 | UniverseSim/ (URP)   |  | core/apps/usim-cli|  | Unreal (C++), Godot       |
                 | rendering, input, UI |  | headless queries  |  | (GDExtension), web (WASM) |
                 +----------------------+  +-------------------+  +---------------------------+
```

Dependencies point one way. Hosts depend on the core, and the core depends on nothing but the C++ standard library. The pipeline and the core share no code. They share the data pack format specification ([docs/formats/DATA_PACK.md](formats/DATA_PACK.md)).

## 2. Repository layout

Items marked (planned) are created during the phases named in the [implementation plan](IMPLEMENTATION_PLAN.md).

```
Universe-Simulation/                    repository root
├── README.md
├── CLAUDE.md                           guidance for AI agents
├── .editorconfig, .clang-format        shared formatting rules
├── docs/
│   ├── SPEC.md                         what and why
│   ├── ARCHITECTURE.md                 how (this file)
│   ├── IMPLEMENTATION_PLAN.md          phased tasks and status
│   ├── DATA_SOURCES.md                 verified source registry (human-readable)
│   ├── adr/                            architecture decision records
│   ├── API.md                          guide for host authors (frame loop, precision, time, bindings)
│   └── formats/DATA_PACK.md            the data pack format, implemented by the pipeline and the core
├── core/                               engine-agnostic simulation (C++20, CMake)
│   ├── CMakeLists.txt, CMakePresets.json
│   ├── cmake/                          shared compiler options
│   ├── include/usim/                   public C++ API headers (usim_core)
│   ├── src/                            usim_core implementation
│   ├── capi/                           universesim shared library
│   │   ├── include/usim/usim.h         the C ABI, the contract for non-C++ hosts
│   │   └── src/
│   ├── apps/usim-cli/                  headless host (usim)
│   ├── tests/                          Catch2 unit tests, one folder per module
│   │   ├── fixtures/                   generated: CSPICE oracle values, synthetic round-trip pack
│   │   ├── cli/                        CLI command tests (separate executable)
│   │   ├── perf/                       allocation test and benchmarks (separate executables)
│   │   └── validation/                 validation tests (ctest label `validation`) on data/
│   └── build/<preset>/                 build output (ignored)
├── tools/
│   ├── pipeline/                       Python data pipeline (section 4): usim-pipeline, usim_pipeline/, tests/
│   └── scripts/                        build-core-for-unity.sh, run-validation.sh
├── data/
│   ├── sources.json                    declared sources (committed)
│   ├── sources.lock.json               pinned files and checksums (committed)
│   ├── catalog/                        packs.json, body selections, manual/ entries (committed)
│   ├── reference/                      validation vectors with provenance; horizons/ API captures (committed)
│   ├── raw/                            downloaded source files (ignored)
│   └── packs/                          built packs (ignored, except the default pack, committed via LFS)
└── UniverseSim/                        Unity 6 project (rendering layer)
    └── Assets/
        ├── UniverseSim/                project-owned Unity content
        │   ├── Plugins/<platform>/     native core library, copied in by script (ignored)
        │   ├── Runtime/                asmdef UniverseSim.Unity: host code and C# bindings
        │   │   └── Native/             P/Invoke declarations mirroring usim.h, CoreSimulation
        │   ├── Editor/                 asmdef UniverseSim.Unity.Editor: plugin import settings, scene builder
        │   ├── Shaders/ UI/ Input/ Materials/   lighting and lines, HUD, input actions
        │   ├── Tests/EditMode/ Tests/PlayMode/  Unity Test Framework tests
        │   ├── Scenes/Main.unity       the main scene (first build scene), built by MainSceneBuilder
        │   └── ATTRIBUTION.md          (Phase 6) sources and licenses of visual assets; v1 has none
        └── StreamingAssets/UniverseSim/   data pack for player builds, copied in by script (ignored)
```

Note: the Unity project folder is named `UniverseSim/`. It was not renamed, so that the Unity Hub registration and the Library cache stay valid.

## 3. The core

### 3.1 Constraints

- **C++20**, built with CMake 3.25 or newer through `core/CMakePresets.json`.
- **The C++ standard library only.** There are no third-party runtime dependencies and no engine headers. Test-only dependencies (Catch2) are fetched by CMake and pinned by SHA-256.
- **No I/O beyond reading a data pack** from a byte buffer or a file path supplied by the host.
- **No global mutable state.** All state hangs off a `Simulation` instance.
- **No heap allocation in per-frame paths.** Snapshot APIs write into caller-provided buffers (`std::span` in C++, pointer plus capacity in the C ABI).
- **Determinism flags:** no fast-math, `-ffp-contract=off` (MSVC `/fp:precise`), and warnings as errors (`core/cmake/UsimCompilerOptions.cmake`).
- **Error handling.** Fallible operations return a result type carrying a value or an error code and message. Exceptions are not used for control flow and never cross the C ABI.

### 3.2 Targets

| Target | Kind | Purpose |
|---|---|---|
| `usim_core` (alias `usim::core`) | static library | The C++ API (namespace `usim`). Used by the CLI, the tests and C++ hosts. |
| `universesim` (alias `usim::capi`) | shared library | The stable C ABI (`usim.h`) for hosts in other languages. Named without a `lib` prefix on every platform. Only `USIM_API` symbols are exported. |
| `usim` | executable | Headless CLI host (FR-CLI-01). |
| `usim_core_tests` | executable | Catch2 unit tests, run through `ctest`. |

### 3.3 Modules

Each module is a subdirectory of `include/usim/` and `src/` with a matching nested namespace.

| Module | Status | Responsibility | Key types |
|---|---|---|---|
| `usim/result.hpp` | Phase 1 | Error model (P1-08) | `Result<T>`, `Result<void>`, `Error`, `ErrorCode` |
| `usim/constants.hpp` | Phase 1 | Definitional constants only, each with a citation | `constants::k_au_km`, `k_c_km_s`, `k_j2000_jd`, `k_tt_minus_tai_s`, `k_eclipj2000_obliquity_rad`, ... |
| `usim::math` | Phase 1 | Double-precision linear algebra and numerics | `Vec3`, `Mat3`, `Quat`, `chebyshev_on_interval`, angle helpers |
| `usim::time` | Phase 1 | Instants, time scales, calendars, ISO 8601 | `TimePoint<Scale>` (`Instant` = TDB, `TtTime`, `TaiTime`), `TimeScales`, `LeapSecondTable`, `TdbModel`, `CalendarDate`, `DateTime`, `Calendar` |
| `usim::frames` | Phase 1 (inertial); body-fixed in P4-03 | Reference frames and transforms; IAU rotation models (pole, prime meridian and their rates) | `FrameId`, `FrameTransform`, `StateVector`, `FrameSystem`, `IauRotationElements`, `IauAngles`, `iau_transform` |
| `usim::catalog` | Phase 1 | Bodies, hierarchy, sourced quantities | `BodyId`, `BodyKind`, `Body`, `BodyCatalog`, `Quantity`, `SourceRef`, `Source` |
| `usim::presentation` | Phase 1 (axes), Phase 4 (view) | Engine-agnostic view math | `AxisConvention`, `View` (view origin, render units, axes), `ScaleMode` |
| `usim::ephemeris` | Phase 3 | Evaluation of ephemeris segments from the pack, chains of segments, coverage | `ChebyshevSegment` (SPK types 2 and 3), `EphemerisChain`, `Evaluation`, `Coverage`, `TimeInterval` |
| `usim::propagation` | Planned (Phase 7) | Non-ephemeris motion | `KeplerPropagator`, `Integrator`, `Dop853`, `Ias15`, `ForceModel` |
| `usim::sim` | Phase 4 | The facade that hosts use; clock; orbit paths | `Simulation`, `SimulationClock`, `ClockLimit`, `BodyState`, `StateQuality`, `SnapshotRequest`, `BodySnapshot`, `OrbitPath`, `OrbitPathCache` |
| `usim::data` | Phase 2 | Data pack reading and validation, SHA-256 | `DataPack`, `DataPackHeader`, `FormatVersion`, `EphemerisSegment`, `PositionApproximation`, `SourceFile`, `sha256` |

### 3.4 Key types

The types below exist; the headers are the reference. [API.md](API.md) explains how hosts use them.

```cpp
namespace usim {

// Fallible operations return Result<T>; the core's public API never throws (usim/result.hpp).
template <typename T> class Result;          // value or Error{ErrorCode code; std::string message}

namespace time {
// A reading of a uniform time scale in two parts: whole days since J2000.0 on that scale plus seconds of
// day in [0, 86400). The scale is part of the type, so a TT reading cannot be passed as TDB.
template <TimeScale Scale> class TimePoint;
using Instant = TimePoint<TimeScale::tdb>;   // the simulation's time; seconds past J2000 equal SPICE ET
using TtTime  = TimePoint<TimeScale::tt>;
using TaiTime = TimePoint<TimeScale::tai>;
// UTC is not uniform (leap seconds), so UTC readings are DateTime values, converted by TimeScales using
// an injected LeapSecondTable and TdbModel (the SPICE DELTET model, parameters from the LSK).
}

namespace catalog {
// Value with provenance. A missing value is explicit, never 0 or NaN.
struct Quantity {
    std::optional<double> value;             // in `unit`, one of the internal units
    Unit unit;
    std::optional<double> uncertainty;       // 1-sigma, same unit, when the source gives one
    SourceRef source;                        // registry id plus locator (file and keyword, or table)
    std::string derivation;                  // formula and inputs for derived values, else empty
};
}

namespace sim {
// The facade hosts use (Phase 4): a pack with its catalog, ephemeris, frames and clock.
class Simulation {
public:
    static Result<Simulation> load_file(const std::filesystem::path& pack);
    const catalog::BodyCatalog& catalog() const;
    SimulationClock& clock();                // TDB instant, signed rate, pause, clamped to coverage()

    Result<BodyState> state(catalog::BodyId body, catalog::BodyId center, frames::FrameId frame,
                            time::Instant t) const;   // BodyState carries StateQuality (model, approximate)
    Result<math::Quat> orientation(catalog::BodyId body, frames::FrameId frame, time::Instant t) const;
    Result<OrbitPath> orbit_path(catalog::BodyId body, catalog::BodyId center, frames::FrameId frame,
                                 time::Instant t, const OrbitPathOptions& options = {}) const;

    // No allocation: every catalog body relative to request.center, in render units and host axes.
    Result<std::size_t> fill_snapshot(const SnapshotRequest& request, std::span<BodySnapshot> out,
                                      std::size_t first = 0) const;
};
}

} // namespace usim
```

Time conventions implemented in Phase 1 (SPEC section 7.2):

- TDB - TT uses the SPICE DELTET model with the parameters from the leap seconds kernel. TT to TDB is a direct evaluation; TDB to TT is solved by fixed-point iteration. Both agree with CSPICE `unitim` to the last bit in the unit tests.
- UTC exists only from the first leap second table entry (1972-01-01). Earlier UTC requests fail with `out_of_range`; CSPICE instead extrapolates.
- In the mixed calendar the dates 1582-10-05 to 1582-10-14 are rejected; CSPICE instead reads them as Gregorian dates.
- Formatting rounds the seconds and carries correctly into minutes, days and leap seconds (23:59:60).

### 3.5 The C ABI (`core/capi/include/usim/usim.h`)

The C ABI is the contract for every host that is not C++. Rules (also in the header):

- Only plain C types: fixed-width integers, `double`, and structs of those. No `bool`, and no enums inside structs (use `int32_t`).
- Every fallible function catches all exceptions and returns an `int32_t` status: `USIM_STATUS_OK` (0) or one of the `USIM_STATUS_*` codes, which equal `usim::ErrorCode` one-to-one (static assertions in `usim_capi.cpp`; `usim_status_name()` returns the name, and the C# `Status` enum is checked against it in a Unity test). The thread-local `usim_last_error_message()` gives details.
- Handles are opaque pointers (`usim_simulation*`) created and destroyed by paired functions.
- Arrays are caller-owned buffers with an explicit capacity and an out-count.
- Strings are UTF-8. The lifetime of each library-owned string is documented, and callers never free them.
- `USIM_ABI_VERSION` increments on any breaking change (version 2 since P4-06, 3 since the stars and rings of 2026-10-22). Hosts check `usim_abi_version()` first and compare their struct layouts with `usim_struct_size()`.
- Thread safety: a simulation handle can be read from several threads only if none of them mutates it (clock changes, orbit paths, destroying). Hosts call mutating functions from one thread.

Version 2 (P4-06) covers, as plain C mirrors of the C++ facade:

| Area | Functions |
|---|---|
| Library | `usim_abi_version`, `usim_version_string`, `usim_status_name`, `usim_last_error_message`, `usim_struct_size` |
| Lifecycle | `usim_simulation_load_file`, `usim_simulation_load_memory`, `usim_simulation_destroy`, `usim_simulation_coverage` |
| Catalog | `usim_body_count`, `usim_body_info_at`, `usim_body_find`, `usim_body_quantity` (value, uncertainty, source, locator, derivation), `usim_body_approximation`, `usim_source_count`, `usim_source_at` |
| Time | `usim_instant_make`, `usim_instant_from_seconds`, `usim_instant_seconds_between`, `usim_time_from_fields`, `usim_time_parse`, `usim_time_format` |
| Clock | `usim_clock_get`, `usim_clock_advance`, `usim_clock_set_time`, `usim_clock_set_rate`, `usim_clock_set_paused` |
| Queries | `usim_state_at`, `usim_orientation_at` |
| Rendering | `usim_fill_snapshot`, `usim_default_orbit_center`, `usim_orbit_path` (cached per simulation) |
| Sky and rings (version 3) | `usim_star_count`, `usim_star_catalog_source`, `usim_fill_stars` (directions at the clock's time in the display frame and host axes), `usim_ring_count`, `usim_ring_at` (radii, optical depth, sources and locators) |

`usim_fill_snapshot` and `usim_fill_stars` convert through a small buffer on the stack, so it allocates nothing and shares no state between threads. The C ABI tests (`core/tests/test_capi.cpp`, and `validation/test_capi_default_pack.cpp` on the default pack) compare every function with the C++ facade, and run under the `asan` preset.

### 3.6 Ephemeris evaluation

- JPL SPK files store Chebyshev polynomial records per segment. DE440 uses SPK type 2 (position only; velocity comes from the derivative). Some satellite kernels use type 3 (position and velocity). The satellite kernels of the moons pack (P6-01) use type 2 only (checked 2026-10-22).
- The pipeline extracts the records from the kernels and writes them into the pack (optionally trimmed to a time window). The core evaluates them. The core does not parse DAF files.
- Each segment gives a target relative to a center (for example Moon relative to the Earth-Moon barycenter). `EphemerisChain` resolves any target and center pair through their common ancestor. For example, Moon relative to Earth = (Moon wrt EMB) - (Earth wrt EMB).
- The authoritative references for record layout and evaluation are the NAIF "SPK Required Reading" and "DAF Required Reading".

Implemented in Phase 3 (`usim::ephemeris`):

- `ChebyshevSegment` is a non-owning view of one pack segment. The record for instant t is found directly: floor((t - init) / interval), clamped to the records, as in SPICE. The Chebyshev argument (t - MID) / RADIUS is computed from the two parts of the `Instant`, so instants given more finely than one double of TDB seconds keep their precision; for an instant made from one double it equals SPICE's (ET - MID). Type 2 velocities are the derivative of the position polynomials. Creating the view checks that every record spans the instants it is used for.
- `EphemerisChain` builds the segment graph once. Where segments of one target overlap, the later one in pack order wins (SPICE precedence). All segments of one target must share one center, so the paths do not change with time. Segments given in ECLIPJ2000 are rotated into the ICRF; output is in the ICRF or ECLIPJ2000. States in body-fixed frames combine the chain with `FrameSystem` (velocity included), which the `Simulation` facade does.
- Coverage (FR-SIM-03) is a `Coverage`: disjoint closed TDB intervals. A body's coverage is the intersection of the segment coverages on its path to the root; a pair's is the intersection on both paths up to the common ancestor. Queries outside coverage fail with `out_of_coverage` and never extrapolate.
- A body with a `PositionApproximation` from the pack has no segments: the chain links it to its proxy (its primary) with a zero offset, and `Evaluation::approximate` (or `is_approximate`) flags every state whose path uses that link.
- `EphemerisChain::state` returns `Result<StateVector>` with a message. `EphemerisChain::evaluate` answers the same query with an `Evaluation` (state, error code and the body that caused it) and never allocates; it is the per-frame path. A test that replaces the global `operator new` proves it (`usim_allocation_tests`).
- Measured (P3-06, release build, Apple Silicon): a snapshot of all 21 bodies of the default pack takes about 4 µs, against the 0.1 ms budget of NFR-03 (`ctest --preset release -L benchmark`).

### 3.7 Data pack (format v1)

The data pack is the contract between the pipeline (Python writer) and the core (C++ reader, `usim::data::DataPack`). Both implement [docs/formats/DATA_PACK.md](formats/DATA_PACK.md), and a round-trip fixture keeps them consistent (`core/tests/fixtures/test_pack_v1.uspack`, written by the pipeline, with its expected contents in `test_pack_expected.hpp`). Contents:

- Header: magic, format version, endianness marker, SHA-256 of the content (verified on load), SHA-256 of the canonical lock file.
- Sections: metadata, string table, sources, source files (from the lock file), bodies, quantities with per-value source and derivation, IAU rotation elements, time data (leap seconds and the TDB - TT model), ephemeris segment descriptors, the Chebyshev records, (format 1.1) approximate positions: bodies without their own segments placed at their primary ([ADR-0008](adr/0008-planet-positions-at-system-barycenters.md)), and (format 1.2) the star catalog and planetary rings ([ADR-0011](adr/0011-sky-background-and-rings.md)).

All multi-byte values are little-endian, and doubles are IEEE 754 binary64. Sections are 8-byte aligned. Minor format versions only add sections, which older readers skip. The core never parses JSON (ADR-0005). `DataPack::parse` validates everything (bounds, codes, units, hierarchy, coverage, TT - TAI = 32.184 s) and returns `data_format` or `incompatible_version` errors; it keeps no pointers into the input buffer.

The default pack, `data/packs/solar-system-1.2.0.uspack` (format 1.2, 33.4 MB, the full span of `de440s.bsp`, with the 9096 stars of the Bright Star Catalogue and Saturn's rings), is committed through Git LFS ([ADR-0006](adr/0006-default-pack-window-and-storage.md)). It places Mars to Pluto at their system barycenters, flagged approximate ([ADR-0008](adr/0008-planet-positions-at-system-barycenters.md)).

The optional moons pack, `data/packs/solar-system-moons-1.0.0.uspack` (304 MB, 2000 to 2100), adds the planet centers and 21 major moons from the JPL satellite ephemerides ([ADR-0010](adr/0010-moons-in-an-optional-pack.md)). It is built locally (`usim-pipeline fetch --optional`, then `build --pack solar-system-moons`) and not committed. The Unity host prefers it when present; the CLI and the validation tests use the default pack unless given another (the moons-pack validation tests use it when it exists).

## 4. Data pipeline: `tools/pipeline/`

A Python package (`usim_pipeline`; Python 3.12.8 from `.python-version`; SpiceyPy 8.2.0, numpy 2.5.3 and pytest 9.1.1 pinned by version and hash in `requirements.txt`, generated by `pin_requirements.py`). It runs only on developer machines (and in CI once it exists). The `tools/pipeline/usim-pipeline` script runs it in its virtual environment (`tools/pipeline/.venv`) from any directory.

| Command | Does |
|---|---|
| `fetch` | Downloads every file declared in `data/sources.json` (optional files only with `--optional` or when a local copy exists: the satellite kernels, 3.4 GB) over HTTPS into `data/raw/`, checks the size against Content-Length, the publisher's MD5 list where one exists (NAIF `aa_checksums.txt`), and records size, SHA-256, MD5 and retrieval time in `data/sources.lock.json`. With an existing lock entry the download must match it; `fetch --update` re-pins after review. A file not yet pinned whose local copy already matches the publisher's MD5 is pinned without downloading it again (large kernels can be fetched ahead of time by `tools/scripts/download-phase6-kernels.sh`, with progress and resume). |
| `verify` | Re-checks `data/raw/` against the lock file. |
| `build` | Builds the packs defined in `data/catalog/packs.json` into `data/packs/<name>-<version>.uspack` (optional packs only when named with `--pack`). Byte-for-byte reproducible: no wall-clock time, deterministic order, derived values computed in decimal arithmetic (no platform libm). |
| `info` | Prints a pack: header, metadata, sources, files, bodies with every value and its source, time data, segment coverage, the star catalog and the rings. |
| `reference` | `unit-fixtures` (core/tests/fixtures/spice_reference.hpp), `test-pack` (the round-trip fixture), `spice` (VAL-01, VAL-03 and VAL-04 vectors and the kernel values the default pack must match; VAL-01 and VAL-04 vectors for the moons pack when the satellite kernels are present), `offline` (all three), `horizons` (VAL-02 captures of the Horizons API, network). Outputs carry provenance headers: tools, kernels with their lock entries, queries, and retrieval time for API captures. |

How `build` assembles a pack:

- **Bodies** come from a selection file (`data/catalog/bodies-v1.json` for the default pack, `bodies-moons.json` for the moons pack: NAIF ID, kind, primary). Names come from the NAIF ID table built into CSPICE (`bodc2n`), title-cased.
- **Constants** come from text kernels: radii and IAU rotation elements from `pck00011.tpc`, GM from `gm_de440.tpc`, leap seconds and the TDB - TT model from `naif0012.tls`. Values are the correctly rounded value of the kernel's text, cross-checked against CSPICE's kernel pool ([ADR-0007](adr/0007-text-kernel-values.md)). Each value records its file and variable.
- **Derived values** record their formula: the mean radius is cbrt(a b c), the radius of the sphere with the ellipsoid's volume.
- **Manual entries** (`data/catalog/manual/*.json`) are accepted only with source id, URL, access date, table and transcriber note, only for declared sources, and never for a field a kernel provides. None exist yet.
- **Ephemeris segments** of SPK types 2 and 3 are read with the DAF routines and copied bit for bit, optionally trimmed to the pack's time window. Only the selected segments, and only their records in the window, are read (the satellite kernels exceed 1 GB). A pack's ephemeris entry can take only the segments relative to given centers (`"centers": [5]`), so a satellite kernel contributes its system and not its copy of the planetary ephemeris.
- **Stars** come from the Bright Star Catalogue (`stars` in a pack definition: `catalog.gz` and its `ReadMe`, whose byte layout the parser checks). **Rings** come from `data/catalog/rings.json`, values transcribed as the text on their source pages and parsed by the pipeline ([ADR-0011](adr/0011-sky-background-and-rings.md)).

CSPICE is an independent oracle for validation. It is never a runtime dependency.

## 5. Host contract

This is how any renderer (Unity now, others later) drives the simulation.

### 5.1 Frame loop

```
every rendered frame:
    usim_clock_advance(sim, real_delta_seconds)       // the core applies rate, pause and coverage clamping
    request = { center: focus body, frame: ECLIPJ2000, origin_km: camera offset from the focus (double),
                km_per_render_unit, axes: host, scale_mode }
    usim_fill_snapshot(sim, &request, buffer, capacity, &count)
    for each snapshot in buffer[0..count]:
        transform.position = (float3) snapshot.render_position   // small magnitude: safe in float
        transform.rotation = (quaternion) snapshot.orientation    // already in host axes
        transform.scale    = snapshot.render_radius
```

### 5.2 Precision strategy

- The core works in double-precision km in the SSB-centered frame.
- The host keeps its camera position in double precision as an offset from the focus body (`SnapshotRequest::center` and `View::origin_km`).
- `fill_snapshot` takes positions relative to the focus body, subtracts the view origin and scales to render units, all in double precision. Values near the camera are small, so the cast to float loses no visible precision. This is a floating-origin scheme: the Unity camera stays near (0, 0, 0) and the world moves around it.
- Far objects are large in magnitude but also far from the camera, so float error there is sub-pixel.
- Depth precision across scales (a planet surface and Neptune in one view) is handled in the host. Options are Unity's reversed-Z buffer, dynamic near and far planes, or layered cameras (a "scaled space" camera for distant objects plus a local camera). This is chosen in Phase 5.

### 5.3 Scale modes

Scale modes are engine-agnostic presentation math in `usim::presentation`:

- `TrueScale`: real radii and real distances.
- `EnlargedBodies(factor)`: radii of planets, dwarf planets and moons multiplied, distances real. The star keeps its true size (enlarged by the same factor it would swallow the inner planets), and a planet and each of its moons are capped at 40 % of their current distance, so a system never overlaps itself (decided in Phase 5; `Simulation::fill_snapshot`).
- The UI always shows the active mode (SPEC P7, FR-REN-02).

### 5.4 Axis conventions

The core is right-handed. A host passes its convention in the snapshot request, and the core returns positions and rotations already mapped. This math lives in `usim::presentation::AxisConvention` and is unit-tested in the core, not in the hosts.

| Host | Convention | Vector mapping from ECLIPJ2000 (X, Y, Z) | Quaternion (w, x, y, z) mapping |
|---|---|---|---|
| Unity | left-handed, Y up | (X, Z, Y) | (w, -x, -z, -y) |
| Godot | right-handed, Y up | (X, Z, -Y) | (w, x, z, -y) |
| Unreal | left-handed, Z up | (X, -Y, Z) | (w, -x, y, -z) |

For the left-handed hosts the axis map S has determinant -1, so rotations transform as R' = S R S, which gives the quaternion formulas in the table. All three were checked numerically against matrix conversion on 2026-10-20. The core's unit tests must still cover them (P1-07). Unreal also uses centimeters, which the scale step handles.

## 6. Unity project: `UniverseSim/`

| Area | Responsibility | Status |
|---|---|---|
| `Assets/UniverseSim/Plugins/<platform>/` | Native core library copied in by `tools/scripts/build-core-for-unity.sh`. Ignored by git, including the `.meta` files. | macOS done |
| `Assets/UniverseSim/Runtime/Native/` | The binding (P5-01): `NativeTypes` (blittable mirrors of the C structs), `NativeMethods` (P/Invoke, `Cdecl`; library-owned strings as `IntPtr`), `CoreLibrary` (ABI version and struct size checks), `CoreSimulation` (a `SafeHandle`-owned simulation with the catalog, provenance, time, clock, snapshots, orbit paths, stars and rings; per-frame calls allocate nothing). | Done |
| `Assets/UniverseSim/Runtime/` (asmdef `UniverseSim.Unity`) | `SimulationHost` (loads the pack from StreamingAssets, or `data/packs` in the Editor; advances the clock; fills the snapshot once per frame), `BodyView` (sphere scaled by the triaxial render radii, oriented, lit per body), `CameraRig` (focus, orbit, logarithmic zoom, transitions, free fly; the floating origin), `OrbitLines` (screen-space lines from the core's paths; a moon's orbit and label appear once the moon stands apart from its planet on screen), `StarField` (the sky background from the core's star directions), `RingSystems` (ring annuli with the core's radii and optical depths), `HudController` (time controls, info panel with ring data, toggles, scale indicator, labels), `CoreStartupCheck`. | Done (Phases 5 and 6) |
| `Assets/UniverseSim/Editor/` (asmdef `UniverseSim.Unity.Editor`) | `NativePluginImportSettings` (plugin import settings), `MainSceneBuilder` (composes the main scene from the assets; menu UniverseSim > Build Main Scene, or `-executeMethod UniverseSim.Unity.Editor.MainSceneBuilder.Build`). | Done |
| `Assets/UniverseSim/Scenes/Main.unity` | The main scene and first build scene, built by `MainSceneBuilder`: camera with `CameraRig`, `UniverseSim` (host, orbit lines, rings and the star field), `HUD` (UI Document). | Done |
| `Assets/UniverseSim/Shaders/` | `BodyLit` (Lambert lighting from the core's per-body light direction, ADR-0009, with the shadow of a body's rings), `Star` (emissive), `Line` (constant pixel width), `StarField` (points of constant pixel size), `Rings` (opacity from the slant optical depth, the planet's shadow). No textures yet (P6-07). | Done |
| `Assets/UniverseSim/UI/` | UI Toolkit: `UniverseSimHud.uxml`, `UniverseSimHud.uss`, the runtime theme and panel settings. | Done |
| `Assets/UniverseSim/Input/` | `UniverseSimControls.inputactions` (Input System): look, drag, zoom, move, pick, focus, free fly, time keys. | Done |
| `Assets/UniverseSim/Tests/EditMode/` | Native load and ABI, struct sizes, `Status` codes, plugin import settings, scene setup, the binding on the default pack (provenance, time, errors, no managed allocations per frame). | Done |
| `Assets/UniverseSim/Tests/PlayMode/` | The main scene end to end: every body shown, positions equal the `usim` CLI, no jitter at 1 km above the Earth or near Pluto, camera transitions and limits; screenshots of several views when `USIM_SCREENSHOT_DIR` is set. Run with graphics (not `-nographics`). | Done |
| `Assets/StreamingAssets/UniverseSim/` | The data packs for player builds (the default pack, and the moons pack if built), copied by `tools/scripts/copy-pack-to-unity.sh`. Ignored by git. | Done |

Rules for Unity code:

- No orbital mechanics, time scale math, frame math or ephemeris logic. If a value is needed, add it to the core and the C ABI.
- Rigidbodies and Unity physics are not used for celestial motion.
- Input goes through the Input System actions asset.
- All visual assets have an entry in `ATTRIBUTION.md`.
- The Editor keeps a native plugin loaded after first use. Restart the Editor after replacing the plugin (OQ-07 decided: no hot reload).

## 7. Testing strategy

| Level | Location | Runs with | Purpose |
|---|---|---|---|
| Unit | `core/tests/` | `ctest --preset dev` (Catch2) | Math, time, frames and IAU rotation, catalog, pack reader, SHA-256, ephemeris, clock, facade, orbit paths, presentation math, the C ABI; the CLI commands in `usim_cli_tests`. Time, frame, quaternion and Chebyshev tests compare against CSPICE values in `core/tests/fixtures/spice_reference.hpp` (`usim-pipeline reference unit-fixtures`). |
| Validation | `core/tests/validation/` (`usim_validation_tests`) | `ctest --preset dev -L validation`, or `tools/scripts/run-validation.sh`, which first gets the pack (LFS checkout or pull, else a pipeline build) | The default pack against `data/reference/`: kernel values match exactly (Phase 2), VAL-03 (Phase 2); VAL-01, VAL-02 (Phase 3), VAL-04 (Phase 4); orbit paths of every body and the C ABI on the real pack; VAL-01 and VAL-04 on the moons pack (Phase 6). They run with the other tests and skip if a pack is missing. |
| Allocation | `core/tests/perf/test_no_allocation.cpp` (`usim_allocation_tests`) | `ctest` | Counts heap allocations through a replaced global `operator new`: the per-frame ephemeris path makes none (NFR-03) |
| Benchmark | `core/tests/perf/bench_snapshot.cpp` (`usim_benchmarks`) | `ctest --preset release -L benchmark` (release builds only) | Catch2 benchmarks of a full snapshot on the default pack, and a check against the NFR-03 budget |
| Pipeline | `tools/pipeline/tests/` | `tools/pipeline/.venv/bin/python -m pytest tools/pipeline` | Registry and lock validation, fetch and verify (fake server), the kernel parser, pack writer and reader, trimming, manual entries, reproducibility, SPK bytes against the raw file, committed fixtures current |
| Pack round trip | `core/tests/data/` | `ctest` | The C++ reader reads the pipeline-written fixture and finds exactly the recorded contents; damaged packs are rejected |
| Unity EditMode | `UniverseSim/Assets/UniverseSim/Tests/EditMode` | Unity Test Runner | Native load, ABI, import settings, adapter mapping |
| Unity PlayMode | `UniverseSim/Assets/UniverseSim/Tests/PlayMode` (planned) | Unity Test Runner | Scene loads, bodies spawn, positions match the CLI |

Unity tests can run headless:

```
<Unity> -batchmode -nographics -projectPath UniverseSim -runTests -testPlatform EditMode -testResults <file>
```

where `<Unity>` is `/Applications/Unity/Hub/Editor/6000.5.1f1/Unity.app/Contents/MacOS/Unity` on macOS.

## 8. Build and integration flow

```
cd core
cmake --preset dev && cmake --build --preset dev && ctest --preset dev    # core, CLI, tests
./build/dev/apps/usim-cli/usim --version
cd ..
tools/scripts/build-core-for-unity.sh                                       # universal plugin into Unity
tools/pipeline/usim-pipeline fetch && tools/pipeline/usim-pipeline build    # data pack (section 4)
```

## 9. Paths to other engines

| Target | Path |
|---|---|
| Unreal | Compile `usim_core` into an Unreal module (C++20 matches Unreal's toolchain), or link the shared library. Use the Unreal axis convention. |
| Godot 4 | A GDExtension (C++) linking `usim_core`, or C# P/Invoke through the C ABI. |
| Custom C++ engines | Link `usim_core` directly. |
| Web | Compile the core with Emscripten to WebAssembly and call the C ABI from JavaScript. |
| Mobile | Static linking. iOS uses `__Internal` P/Invoke in Unity. |

Keeping the core free of engine types, the C ABI small and stable, and the data pack format documented is what keeps these paths open.
