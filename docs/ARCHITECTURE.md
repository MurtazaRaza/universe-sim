# Architecture

| | |
|---|---|
| Status | Draft v0.3 |
| Last updated | 2026-10-20 |
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

Dependencies point one way. Hosts depend on the core, and the core depends on nothing but the C++ standard library. The pipeline and the core share no code. They share the data pack format specification (`docs/formats/DATA_PACK.md`, Phase 2).

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
│   └── formats/                        (planned) DATA_PACK.md and other file format specs
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
│   │   └── fixtures/                   generated reference data (CSPICE oracle values)
│   └── build/<preset>/                 build output (ignored)
├── tools/
│   ├── pipeline/                       (planned, Phase 2) Python: fetch, verify, build, info, reference
│   └── scripts/                        build-core-for-unity.sh, generate_spice_test_fixtures.py
├── data/                               (planned, Phase 2)
│   ├── sources.json                    declared sources (committed)
│   ├── sources.lock.json               resolved files and checksums (committed)
│   ├── catalog/                        body selection and manual entries (committed)
│   ├── reference/                      validation vectors with provenance (committed)
│   ├── raw/                            downloaded source files (ignored)
│   └── packs/                          generated data packs (ignored)
└── UniverseSim/                        Unity 6 project (rendering layer)
    └── Assets/
        ├── UniverseSim/                project-owned Unity content
        │   ├── Plugins/<platform>/     native core library, copied in by script (ignored)
        │   ├── Runtime/                asmdef UniverseSim.Unity: host code and C# bindings
        │   │   └── Native/             P/Invoke declarations mirroring usim.h
        │   ├── Editor/                 asmdef UniverseSim.Unity.Editor: plugin import settings, tools
        │   ├── Tests/EditMode/         Unity Test Framework tests
        │   ├── Scenes/Main.unity       the main scene (first build scene)
        │   ├── Materials/ Shaders/ Textures/ UI/   (planned)
        │   └── ATTRIBUTION.md          (planned) sources and licenses of visual assets
        └── StreamingAssets/UniverseSim/   (planned) data pack, copied in by script (ignored)
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
| `usim::frames` | Phase 1 (inertial); body-fixed in P4-03 | Reference frames and transforms | `FrameId`, `FrameTransform`, `StateVector`, `FrameSystem` |
| `usim::catalog` | Phase 1 | Bodies, hierarchy, sourced quantities | `BodyId`, `BodyKind`, `Body`, `BodyCatalog`, `Quantity`, `SourceRef`, `Source` |
| `usim::presentation` | Phase 1 (axes); more in Phase 4 | Engine-agnostic view math | `AxisConvention`; planned: `ViewOrigin`, `ScaleMode`, `OrbitPathSampler` |
| `usim::ephemeris` | Planned (Phase 3) | Evaluation of ephemeris segments from the pack | `ChebyshevSegment` (SPK types 2 and 3), `EphemerisChain`, `Coverage` |
| `usim::propagation` | Planned (Phase 7) | Non-ephemeris motion | `KeplerPropagator`, `Integrator`, `Dop853`, `Ias15`, `ForceModel` |
| `usim::sim` | Planned (Phase 4) | The facade that hosts use | `Simulation`, `SimulationClock`, `BodyState`, `StateQuality` |
| `usim::data` | Planned (Phase 2) | Data pack reading | `DataPackReader`, `DataPackHeader`, `FormatVersion` |

### 3.4 Key types

The Phase 1 types below exist; the headers are the reference. The Phase 3 and 4 types are sketches and not binding.

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

// Sketches (Phase 3 and 4):
struct BodyState {
    catalog::BodyId body;
    math::Vec3 position_km;                  // relative to the requested center, in the requested frame
    math::Vec3 velocity_km_s;
    StateQuality quality;                    // Ephemeris, Integrated, Analytic or OutOfCoverage
};

class Simulation {
public:
    static Result<Simulation> load(std::span<const std::byte> pack);
    const catalog::BodyCatalog& catalog() const;
    SimulationClock& clock();

    Result<BodyState> state(catalog::BodyId target, catalog::BodyId center, frames::FrameId frame,
                            time::Instant t) const;
    Result<math::Quat> orientation(catalog::BodyId body, frames::FrameId frame, time::Instant t) const;

    // No allocation: fills the caller's buffer for the current clock time, returns the count.
    std::size_t fill_snapshot(const SnapshotRequest& request, std::span<BodySnapshot> out) const;
};

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
- Every fallible function catches all exceptions and returns an `int32_t` status: `USIM_STATUS_OK` (0) or one of the `USIM_STATUS_*` codes, which equal `usim::ErrorCode` one-to-one (static assertions in `usim_capi.cpp`; `usim_status_name()` returns the name, and the C# `Status` enum is checked against it in a Unity test). A thread-local `usim_last_error_message()` (planned) gives details.
- Handles are opaque pointers (`usim_simulation*`) created and destroyed by paired functions.
- Arrays are caller-owned buffers with an explicit capacity and an out-count.
- Strings are UTF-8. The lifetime of each library-owned string is documented, and callers never free them.
- `USIM_ABI_VERSION` increments on any breaking change. Hosts check `usim_abi_version()` first. The ABI also exposes the size of every public struct (planned), so bindings can verify their layouts at startup.
- Thread safety: a simulation handle can be read from several threads only if none of them mutates it (clock changes, loading). Hosts call mutating functions from one thread.

Planned shape (illustrative):

```c
typedef struct usim_simulation usim_simulation;            /* opaque */
typedef struct usim_vec3d { double x, y, z; } usim_vec3d;
typedef struct usim_body_snapshot {
    int32_t    id_namespace;  int32_t reserved;  int64_t id_value;
    usim_vec3d render_position;                           /* relative to the view origin, host axes */
    double     orientation[4];                            /* quaternion w, x, y, z in host axes */
    double     render_radius;
    uint32_t   flags;                                     /* quality, out-of-coverage, display-only */
    uint32_t   reserved2;
} usim_body_snapshot;

USIM_API int32_t usim_simulation_load_file(const char* utf8_path, usim_simulation** out);
USIM_API void    usim_simulation_destroy(usim_simulation* sim);
USIM_API int32_t usim_clock_advance(usim_simulation* sim, double real_seconds);
USIM_API int32_t usim_fill_snapshot(const usim_simulation* sim, const usim_snapshot_request* request,
                                    usim_body_snapshot* out, int32_t capacity, int32_t* out_count);
```

### 3.6 Ephemeris evaluation

- JPL SPK files store Chebyshev polynomial records per segment. DE440 uses SPK type 2 (position only; velocity comes from the derivative). Some satellite kernels use type 3 (position and velocity). Phase 6 checks which other types the selected satellite kernels use.
- The pipeline extracts the records from the kernels and writes them into the pack (optionally trimmed to a time window). The core evaluates them. The core does not parse DAF files.
- Each segment gives a target relative to a center (for example Moon relative to the Earth-Moon barycenter). `EphemerisChain` resolves any target and center pair through their common ancestor. For example, Moon relative to Earth = (Moon wrt EMB) - (Earth wrt EMB).
- The authoritative references for record layout and evaluation are the NAIF "SPK Required Reading" and "DAF Required Reading".

### 3.7 Data pack (planned, format v1)

The data pack is the contract between the pipeline (Python writer) and the core (C++ reader). Both implement `docs/formats/DATA_PACK.md` (written in Phase 2), and round-trip tests keep them consistent. Planned contents:

- Header: magic bytes, format version, endianness marker, hash of `sources.lock.json`, content hash.
- String table.
- Source table: the registry entries used, with citations and checksums.
- Catalog: bodies, hierarchy and `Quantity` fields with source references.
- Rotation models: IAU coefficients per body.
- Leap second table.
- Ephemeris segments: Chebyshev records, optionally trimmed to a time window.

All multi-byte values are little-endian, and doubles are IEEE 754 binary64. The core never parses JSON (ADR-0005).

## 4. Data pipeline: `tools/pipeline/` (planned, Phase 2)

A Python package (Python 3.12 or newer, pinned to 3.12.8 in `.python-version`; dependencies pinned in `requirements.txt`, for example SpiceyPy and numpy). It runs only on developer machines (and in CI once it exists).

| Command | Does |
|---|---|
| `fetch` | Downloads every file in `data/sources.json` into `data/raw/`, verifies the publisher checksum (where one exists) and the SHA-256, and writes `data/sources.lock.json` (URL, size, hashes, retrieval time). With an existing lock file it verifies instead of updating. |
| `verify` | Re-checks `data/raw/` against the lock file. |
| `build` | Parses the kernels (through SpiceyPy/CSPICE) and the catalog inputs, derives values (recording their derivations), extracts and trims the ephemeris segments, and writes `data/packs/<name>-<version>.uspack`. The output is byte-for-byte reproducible. |
| `info` | Prints the contents of a pack: bodies, coverage, sources, sizes. |
| `reference` | Generates validation vectors with CSPICE from the same kernels, and captures Horizons API responses, into `data/reference/`. Every file has a header recording the tool versions, kernels, query parameters and retrieval date. |

CSPICE is an independent oracle for validation. It is never a runtime dependency.

## 5. Host contract

This is how any renderer (Unity now, others later) drives the simulation.

### 5.1 Frame loop

```
every rendered frame:
    usim_clock_advance(sim, real_delta_seconds)       // the core applies rate, pause and coverage clamping
    request = { frame: ECLIPJ2000, view_origin: focus (double), scale_mode, axis_convention: host }
    usim_fill_snapshot(sim, &request, buffer, capacity, &count)
    for each snapshot in buffer[0..count]:
        transform.position = (float3) snapshot.render_position   // small magnitude: safe in float
        transform.rotation = (quaternion) snapshot.orientation    // already in host axes
        transform.scale    = snapshot.render_radius
```

### 5.2 Precision strategy

- The core works in double-precision km in the SSB-centered frame.
- The host keeps its camera and focus positions in double precision, either in the core's `ViewOrigin` or in a double-precision field in the host.
- `fill_snapshot` returns positions **relative to the view origin**, already scaled to render units. Values near the camera are small, so the cast to float loses no visible precision. This is a floating-origin scheme: the Unity camera stays near (0, 0, 0) and the world moves around it.
- Far objects are large in magnitude but also far from the camera, so float error there is sub-pixel.
- Depth precision across scales (a planet surface and Neptune in one view) is handled in the host. Options are Unity's reversed-Z buffer, dynamic near and far planes, or layered cameras (a "scaled space" camera for distant objects plus a local camera). This is chosen in Phase 5.

### 5.3 Scale modes

Scale modes are engine-agnostic presentation math in `usim::presentation`:

- `TrueScale`: real radii and real distances.
- `EnlargedBodies(factor)`: radii multiplied, distances real. Moons may need their own rule so they do not end up inside their enlarged primary. This is a Phase 5 design point.
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
| `Assets/UniverseSim/Runtime/Native/` | P/Invoke declarations (`NativeMethods`) mirroring `usim.h`, plus safe wrappers (`CoreLibrary`). `CallingConvention.Cdecl`. Library-owned strings are marshalled as `IntPtr`. | Started |
| `Assets/UniverseSim/Runtime/` (asmdef `UniverseSim.Unity`) | `CoreStartupCheck` (logs the core version and verifies the ABI at startup). Planned: `SimulationHost` (loads the pack from StreamingAssets, owns the simulation handle, runs the frame loop), `BodyView`, `CameraRig`, `OrbitPathView`, `LabelView`, the time controls and info panel controllers. | Started |
| `Assets/UniverseSim/Editor/` (asmdef `UniverseSim.Unity.Editor`) | `NativePluginImportSettings`: enforces plugin import settings on import and after each domain reload. Planned: menu items to build the core and copy the data pack. | Started |
| `Assets/UniverseSim/Scenes/Main.unity` | The main scene and first build scene (camera, directional light, global volume from the URP template). | Done |
| `Assets/UniverseSim/Tests/EditMode/` | Native library load and ABI check, C# `Status` codes match the core, plugin import settings, main scene in build settings with no missing scripts. Planned: snapshot-to-transform mapping. | Started |
| `Assets/UniverseSim/UI/` | UI Toolkit UXML and USS files. | Planned |
| `Assets/UniverseSim/Shaders/` | Sun-direction lighting for bodies (URP point lights cannot cover system distances), rings, atmosphere (visual only). | Planned |
| `Assets/StreamingAssets/UniverseSim/` | The data pack, copied by script. Ignored by git. | Planned |

Rules for Unity code:

- No orbital mechanics, time scale math, frame math or ephemeris logic. If a value is needed, add it to the core and the C ABI.
- Rigidbodies and Unity physics are not used for celestial motion.
- Input goes through the Input System actions asset.
- All visual assets have an entry in `ATTRIBUTION.md`.
- The Editor keeps a native plugin loaded after first use. Restart the Editor after replacing the plugin (hot reload is OQ-07).

## 7. Testing strategy

| Level | Location | Runs with | Purpose |
|---|---|---|---|
| Unit | `core/tests/` | `ctest --preset dev` (Catch2) | Math, time, frames, catalog, pack reader, presentation math, the C ABI. Time, frame, quaternion and Chebyshev tests compare against CSPICE values in `core/tests/fixtures/spice_reference.hpp`, generated by `tools/scripts/generate_spice_test_fixtures.py` (pinned SpiceyPy; P2-10 takes this over). |
| Validation | `core/tests/validation/` (planned) | `ctest --preset dev -L validation` | VAL-01 to VAL-07 against `data/reference/`. Needs a built pack. |
| Pipeline | `tools/pipeline/tests/` (planned) | `pytest` | Checksum verification, kernel extraction, pack writer, reproducibility |
| Pack round trip | `core/tests/` (planned) | `ctest` | The C++ reader reads what the Python writer wrote |
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
python -m pipeline fetch && python -m pipeline build                        # (planned, Phase 2)
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
