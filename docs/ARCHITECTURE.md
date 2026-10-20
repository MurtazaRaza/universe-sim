# Architecture

| | |
|---|---|
| Status | Draft v0.1 |
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
  +------------------------------+          +-----------------------------+
  | tools/UniverseSim.DataPipeline|  build  | data/packs/*.uspack         |
  | (.NET console, net10.0)       |-------> | versioned binary data pack  |
  +------------------------------+          | ephemeris + catalog +       |
            |                               | provenance                  |
            | reference vectors             +-----------------------------+
            v                                            |
  data/reference/ (committed, small)                     | load
            |                                            v
            |                  +-------------------------------------------------+
            +----------------> | core/src/UniverseSim.Core (netstandard2.1)       |
               validation      | zero dependencies, no engine references          |
               tests           |                                                  |
                               | Mathematics | Time | Frames | Catalog | Ephemeris |
                               | Propagation | Simulation | Presentation | Data    |
                               +-------------------------------------------------+
                                   |                   |                    |
                     prebuilt DLL  |                   |                    |
                                   v                   v                    v
                     +----------------------+  +----------------+  +-----------------------+
                     | Unity host           |  | CLI host       |  | Future hosts          |
                     | UniverseSim/ (URP)   |  | tools/         |  | Godot (C#), C++ via   |
                     | rendering, input, UI |  | UniverseSim.Cli|  | NativeAOT C ABI, web  |
                     +----------------------+  +----------------+  +-----------------------+
```

Dependencies point one way: hosts depend on the core, the core depends on nothing, and the pipeline depends on the core (to share readers and models).

## 2. Repository layout

Items marked (planned) are created during the phases named in the [implementation plan](IMPLEMENTATION_PLAN.md).

```
Universe-Simulation/                 repository root
├── README.md
├── CLAUDE.md                        guidance for AI agents
├── docs/
│   ├── SPEC.md                      what and why
│   ├── ARCHITECTURE.md              how (this file)
│   ├── IMPLEMENTATION_PLAN.md       phased tasks and status
│   ├── DATA_SOURCES.md              verified source registry (human-readable)
│   ├── adr/                         architecture decision records
│   └── formats/                     (planned) DATA_PACK.md and other file format specs
├── core/                            (planned, Phase 0) engine-agnostic simulation
│   ├── UniverseSim.sln
│   ├── Directory.Build.props        shared compiler settings
│   ├── src/UniverseSim.Core/        netstandard2.1, zero dependencies
│   └── tests/
│       ├── UniverseSim.Core.Tests/        unit tests (NUnit)
│       └── UniverseSim.Validation.Tests/  tests against data/reference
├── tools/                           (planned)
│   ├── UniverseSim.DataPipeline/    fetch, verify, build, info
│   ├── UniverseSim.Cli/             headless host
│   ├── reference/                   Python + SpiceyPy scripts that generate reference vectors
│   └── scripts/                     build-core-for-unity, copy-pack-to-unity, ...
├── data/                            (planned, Phase 2)
│   ├── sources.json                 declared sources (committed)
│   ├── sources.lock.json            resolved files and checksums (committed)
│   ├── catalog/                     body selection and manual entries (committed)
│   ├── reference/                   validation vectors with provenance (committed)
│   ├── raw/                         downloaded source files (ignored)
│   └── packs/                       generated data packs (ignored)
└── UniverseSim/                     Unity 6 project (rendering layer)
    └── Assets/
        ├── Plugins/UniverseSim.Core/    (planned) core DLL copied in; .meta committed, DLL ignored
        ├── UniverseSim/                 (planned) project-owned Unity code and assets
        │   ├── Runtime/                 asmdef UniverseSim.Unity
        │   ├── Editor/                  asmdef UniverseSim.Unity.Editor
        │   ├── Tests/                   EditMode and PlayMode tests
        │   ├── Scenes/ Materials/ Shaders/ Textures/ UI/
        │   └── ATTRIBUTION.md           sources and licenses of visual assets
        └── StreamingAssets/UniverseSim/ (planned) data pack copied in (ignored)
```

Note: the Unity project folder is named `UniverseSim/` and the core assembly is named `UniverseSim.Core`. They are different things. The Unity project was not renamed, so that the Unity Hub registration and the Library cache stay valid.

## 3. The core: `UniverseSim.Core`

### 3.1 Constraints

- Target: `netstandard2.1` (the profile Unity supports; see [ADR-0002](adr/0002-core-language-and-unity-integration.md)).
- `LangVersion` 9.0. This keeps the source compatible with Unity's C# compiler in case the core is ever compiled inside Unity.
- Nullable reference types enabled, warnings as errors, deterministic builds.
- **No third-party packages. No engine references. No I/O beyond reading a data pack from a `Stream` or file path supplied by the host.**
- No static mutable state. Every piece of state hangs off a `Simulation` instance.
- Hot paths do not allocate: snapshot APIs write into caller-provided `Span<T>` buffers.

### 3.2 Namespaces (planned)

| Namespace | Responsibility | Key types (illustrative) |
|---|---|---|
| `UniverseSim.Core.Mathematics` | Double-precision linear algebra and numerics | `Vector3d`, `Matrix3x3d`, `Quaterniond`, `Chebyshev`, `Angle` |
| `UniverseSim.Core.Time` | Instants, time scales, calendars | `Instant` (TDB, two-part), `TimeScale`, `LeapSecondTable`, `JulianDate`, `CalendarDate` |
| `UniverseSim.Core.Frames` | Reference frames and transforms | `FrameId`, `FrameTransform`, `StandardFrames`, `RotationModel` |
| `UniverseSim.Core.Catalog` | Bodies, hierarchy, sourced quantities | `BodyId`, `BodyKind`, `Body`, `BodyCatalog`, `Quantity`, `SourceRef`, `Source` |
| `UniverseSim.Core.Ephemeris` | Ephemeris evaluation | `IEphemerisProvider`, `DafReader`, `SpkSegment`, `SpkType2Segment`, `SpkType3Segment`, `EphemerisChain` |
| `UniverseSim.Core.Propagation` | Non-ephemeris motion (Phase 7+) | `KeplerPropagator`, `IIntegrator`, `Dop853`, `Ias15`, `IForceModel` |
| `UniverseSim.Core.Simulation` | The facade that hosts use | `Simulation`, `SimulationClock`, `BodyState`, `StateQuality`, `QueryResult` |
| `UniverseSim.Core.Presentation` | Engine-agnostic view math | `ViewOrigin`, `ScaleMode`, `AxisConvention`, `OrbitPathSampler` |
| `UniverseSim.Core.Data` | Data pack reading | `DataPackReader`, `DataPackHeader`, `DataPackVersion` |
| `UniverseSim.Core.Constants` | Definitional constants only, each with a citation | `Constants` |

### 3.3 Key type sketches (illustrative, not binding)

```csharp
// Time: TDB instant stored in two parts to keep resolution over millennia.
public readonly struct Instant : IComparable<Instant>
{
    public long DaysSinceJ2000 { get; }      // whole TDB days relative to J2000.0
    public double SecondsOfDay { get; }      // [0, 86400)
    public double ToSecondsSinceJ2000();     // for evaluation; fine within the DE440 span
}

// Value with provenance. A missing value is represented explicitly, never as 0 or NaN.
public readonly struct Quantity
{
    public bool HasValue { get; }
    public double Value { get; }             // in the canonical unit for this field
    public double? Uncertainty { get; }      // 1-sigma, same unit, when the source provides it
    public SourceRef Source { get; }         // registry id plus locator (file, table, keyword)
    public bool IsDerived { get; }           // computed by the pipeline from other quantities
}

public readonly struct BodyState
{
    public BodyId Body { get; }
    public Vector3d PositionKm { get; }      // relative to the requested center, in the requested frame
    public Vector3d VelocityKmPerS { get; }
    public StateQuality Quality { get; }     // Ephemeris | Integrated | Analytic | OutOfCoverage
}

public sealed class Simulation
{
    public static Simulation Load(Stream dataPack);
    public BodyCatalog Catalog { get; }
    public SimulationClock Clock { get; }

    public QueryResult<BodyState> GetState(BodyId target, BodyId center, FrameId frame, Instant t);
    public QueryResult<Quaterniond> GetOrientation(BodyId body, FrameId frame, Instant t);

    // Zero-allocation snapshot for hosts: fills caller-owned buffers for the current clock time.
    public int FillSnapshot(in SnapshotRequest request, Span<BodySnapshot> output);
}
```

### 3.4 Ephemeris evaluation

- JPL SPK files are DAF containers holding segments. DE440 uses SPK type 2 (Chebyshev, position only; velocity comes from the derivative). Some satellite kernels use type 3 (Chebyshev, position and velocity). Phase 6 checks which other types the selected satellite kernels use.
- Each segment gives a target relative to a center (for example Moon relative to the Earth-Moon barycenter). `EphemerisChain` resolves any target and center pair through their common ancestor in the segment graph. For example, Moon relative to Earth = (Moon wrt EMB) - (Earth wrt EMB).
- The authoritative references are the NAIF "DAF Required Reading" and "SPK Required Reading" documents.
- The DAF/SPK reader lives in the core so the pipeline (which reads raw kernels) and the runtime (which reads trimmed segments from the data pack) share one implementation.

### 3.5 Data pack (planned, format v1)

The data pack is the contract between the pipeline and the runtime. It also lets non-.NET ports reuse the data without reimplementing the pipeline. Its format is specified in `docs/formats/DATA_PACK.md` (written in Phase 2). Planned contents:

- Header: magic bytes, format version, build timestamp, hash of `sources.lock.json`, content hash.
- String table.
- Source table: the registry entries used, with citations and checksums.
- Catalog: bodies, hierarchy and `Quantity` fields with source references.
- Rotation models: IAU coefficients per body.
- Leap second table.
- Ephemeris segments: Chebyshev records copied from SPK type 2/3 segments, optionally trimmed to a time window.

The core does not parse JSON. Human-readable inputs (`data/catalog/*.json`, `data/sources.json`) are compiled into the binary pack by the pipeline, which keeps the core free of dependencies.

## 4. Data pipeline: `tools/UniverseSim.DataPipeline` (planned)

A .NET console application (`net10.0`, free to use NuGet packages) that references the core.

| Command | Does |
|---|---|
| `fetch` | Downloads every file in `data/sources.json` into `data/raw/`, verifies the publisher checksum (where one exists) and the SHA-256, and writes `data/sources.lock.json` (URL, size, hashes, retrieval time). With an existing lock file, it verifies instead of updating. |
| `verify` | Re-checks `data/raw/` against the lock file. |
| `build` | Parses kernels and catalog inputs, derives values (recording derivations), trims ephemeris segments to the configured window, and writes `data/packs/<name>-<version>.uspack`. The output is byte-for-byte reproducible. |
| `info` | Prints the contents of a pack: bodies, coverage, sources, sizes. |

Reference vectors are generated separately by `tools/reference/` (Python with SpiceyPy, pinned in `requirements.txt`). The project uses CSPICE as an independent oracle; it is never a runtime dependency. Horizons API snapshots are also captured there. Every reference file includes a header recording the tool versions, kernels, query parameters and retrieval date.

## 5. Host contract

This is how any renderer (Unity now, others later) drives the simulation.

### 5.1 Frame loop

```
every rendered frame:
    sim.Clock.Advance(realDeltaSeconds)            // core applies rate, pause and coverage clamping
    viewOrigin = camera focus position (double, from the core)
    n = sim.FillSnapshot(request{ frame: ECLIPJ2000, origin: viewOrigin, scale: scaleMode }, buffer)
    for each BodySnapshot in buffer[0..n]:
        host position = AxisConvention.Map(snapshot.RenderPosition)   // still double
        transform.position = (float3) host position                    // small magnitude: safe in float
        transform.rotation = AxisConvention.Map(snapshot.Orientation)
        transform.scale    = snapshot.RenderRadius
```

### 5.2 Precision strategy

- The core works in double-precision km in the SSB-centered frame.
- The host keeps its camera and focus positions in double precision, either in the core's `ViewOrigin` or in a double-precision field in the adapter.
- `FillSnapshot` returns positions **relative to the view origin**, already scaled to render units. Values near the camera are small, so the cast to float loses no visible precision. This is a floating-origin scheme: the Unity camera stays near (0, 0, 0) and the world moves around it.
- Far objects are large in magnitude but also far from the camera, so float error there is sub-pixel.
- Depth precision across scales (a planet surface and Neptune in one view) is handled in the host. Options are Unity's reversed-Z buffer, dynamic near and far planes, or layered cameras (a "scaled space" camera for distant objects plus a local camera). This is chosen in Phase 5.

### 5.3 Scale modes

Scale modes are engine-agnostic presentation math in `UniverseSim.Core.Presentation`:

- `TrueScale`: real radii and real distances.
- `EnlargedBodies(factor)`: radii multiplied, distances real. Moons may need their own rule so they do not end up inside their enlarged primary. This is a Phase 5 design point.
- The UI always shows the active mode (SPEC P7, FR-REN-02).

### 5.4 Axis conventions

The core is right-handed. Each host declares its convention, and `AxisConvention` maps vectors and rotations. This math is unit-tested in the core, not in the host.

| Host | Convention | Mapping from ECLIPJ2000 (X, Y, Z) |
|---|---|---|
| Unity | left-handed, Y up | (X, Z, Y): ecliptic north becomes Unity up |
| Godot | right-handed, Y up | (X, Z, -Y) |

For Unity, the axis map S (which swaps Y and Z) has determinant -1, so rotations transform as R' = S R S. For a quaternion (w, x, y, z) this gives (w, -x, -z, -y). For Godot the map is a proper rotation, so the quaternion's vector part maps like a vector: (w, x, z, -y). Both formulas were checked numerically against matrix conversion on 2026-10-20. The core's unit tests must still cover them (P1-07).

## 6. Unity project: `UniverseSim/` (planned structure)

| Area | Responsibility |
|---|---|
| `Plugins/UniverseSim.Core/` | Prebuilt core DLL, PDB and XML docs, copied by `tools/scripts/build-core-for-unity`. Only `.meta` files are committed. |
| `UniverseSim/Runtime/` (asmdef `UniverseSim.Unity`) | `SimulationHost` (loads the pack from StreamingAssets, owns the `Simulation`, runs the frame loop), `BodyView`, `CameraRig`, `OrbitPathView`, `LabelView`, the time controls and info panel controllers. |
| `UniverseSim/Editor/` | Menu items: build and copy the core, copy the data pack, validate the scene. |
| `UniverseSim/UI/` | UI Toolkit UXML and USS files. |
| `UniverseSim/Shaders/` | Sun-direction lighting for bodies (URP point lights cannot cover system distances), rings, atmosphere (visual only). |
| `UniverseSim/Tests/` | EditMode tests (snapshot to transform mapping) and PlayMode smoke tests. |
| `StreamingAssets/UniverseSim/` | The data pack, copied by script. Ignored by git. |

Rules for Unity code:

- No orbital mechanics, time scale math or ephemeris logic. If a value is needed, add it to the core API.
- Rigidbodies and Unity physics are not used for celestial motion.
- Input goes through the Input System actions asset.
- All visual assets have an entry in `ATTRIBUTION.md`.

## 7. Testing strategy

| Level | Location | Runs with | Purpose |
|---|---|---|---|
| Unit | `core/tests/UniverseSim.Core.Tests` | `dotnet test` (NUnit) | Math, time, frames, catalog, readers, presentation math |
| Validation | `core/tests/UniverseSim.Validation.Tests` | `dotnet test --filter Category=Validation` | VAL-01 to VAL-07 against `data/reference/`. Needs a built pack. |
| Pipeline | `tools/.../Tests` (planned) | `dotnet test` | Checksum verification, kernel parsing, reproducibility |
| Unity EditMode | `UniverseSim/Assets/UniverseSim/Tests/EditMode` | Unity Test Runner | Adapter mapping, host lifecycle |
| Unity PlayMode | `UniverseSim/Assets/UniverseSim/Tests/PlayMode` | Unity Test Runner | Scene loads, bodies spawn, positions match the CLI |

NUnit is used on both sides because the Unity Test Framework is built on NUnit.

## 8. Build and integration flow (planned)

```
dotnet build core/UniverseSim.sln -c Release
dotnet test  core/UniverseSim.sln
dotnet run --project tools/UniverseSim.DataPipeline -- fetch
dotnet run --project tools/UniverseSim.DataPipeline -- build --profile default
tools/scripts/build-core-for-unity.sh      # copies DLL/PDB/XML into UniverseSim/Assets/Plugins/UniverseSim.Core/
tools/scripts/copy-pack-to-unity.sh        # copies the pack into UniverseSim/Assets/StreamingAssets/UniverseSim/
```

Exact commands go into `CLAUDE.md` and `README.md` once they exist.

## 9. Paths to other engines

| Target | Path |
|---|---|
| Godot 4 (C#) | Reference the core assembly directly. Use `AxisConvention` for Godot. |
| Unreal, custom C++ | Multi-target the core to `net10.0` and publish it as a NativeAOT shared library with a C ABI (`UnmanagedCallersOnly` exports). Alternatively, port the evaluator to C++ using the documented data pack format. |
| Web | .NET WebAssembly, or a JavaScript or TypeScript port that reads the same data pack. |

Keeping the core free of engine types, the data pack documented, and the host contract small is what keeps these paths open.
