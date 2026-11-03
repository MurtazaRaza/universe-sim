# CLAUDE.md

Guidance for AI agents (and humans) working in this repository.

## What this project is

A universe simulation built on real, verified data. It starts with the Solar System and expands outward. The simulation is an engine-agnostic **C++20** library (`core/`) with a stable C ABI. Unity is only the rendering layer and talks to the core through P/Invoke. Read the documents below before making changes.

## Read first

1. [docs/SPEC.md](docs/SPEC.md): requirements, conventions (units, time, frames, IDs), accuracy targets.
2. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): modules, the C ABI, the host contract, repository layout.
3. [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md): phases and tasks. Find the current phase and the next unchecked task.
4. [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md): the registry of allowed data sources and their tiers.
5. [docs/adr/](docs/adr/): why the key decisions were made (ADR-0002 covers C++ and the Unity integration).
6. [docs/API.md](docs/API.md): how hosts use the core (frame loop, precision, time, bindings).
7. [docs/ART_DIRECTION.md](docs/ART_DIRECTION.md): the look of the stylized style (Track A), with its palette and parameters.

## Non-negotiable rules

1. **The engine boundary.**
   - Nothing under `core/` includes engine headers or depends on an engine.
   - Unity code under `UniverseSim/` never does orbital mechanics, time scale conversion, frame math or ephemeris evaluation.
   - If the renderer needs a value, add it to the core and expose it through the C ABI.
2. **Real data only.**
   - Never type a physical value (mass, GM, radius, orbital element, rotation rate, albedo, and so on) into code or data from memory.
   - Values enter through the data pipeline from a source registered in `docs/DATA_SOURCES.md` and `data/sources.json`.
   - The only exceptions are definitional constants (for example the au, the speed of light, J2000.0, TT - TAI) in `core/include/usim/constants.hpp`, each with a citation.
   - If a value cannot be sourced, leave it missing and say so. Do not estimate.
   - This rule covers data, not looks. The art layer may stylize the look (a little toon-like) and exaggerate effects within SPEC section 11.1 ([ADR-0015](docs/adr/0015-stylized-presentation-over-real-data.md)). It never changes where, when or what something is, or any value the UI shows, and every exaggeration is off in the realistic style.
3. **Provenance travels with the data.** Every catalog value carries its source reference. Derived values record their derivation.
4. **Units, frames and time are explicit.**
   - Internally: km, s, km/s, rad, GM in km^3/s^2, TDB, and the ICRF as the native frame.
   - Put units in names (`position_km`, `velocity_km_s`) or doc comments.
   - Convert only at the boundaries (presentation and user input).
5. **Precision.**
   - Use `double` in the core. Single precision exists only in host code, after re-centering on the view origin.
   - Axis and handedness mapping happens in `usim::presentation::AxisConvention`, never by hand in host code.
6. **Validate numerical work.**
   - Numerical changes come with tests.
   - Validation tests must pass.
   - Never loosen a threshold without recording the measured cause in the plan or an ADR.
7. **The core uses the C++ standard library only.** No third-party runtime dependencies. Test-only dependencies are fetched by CMake and pinned by SHA-256.
8. **C ABI discipline** (`core/capi/include/usim/usim.h`):
   - Plain C types only, and no exception may escape.
   - Arrays are caller-owned buffers; string lifetimes are documented.
   - Increment `USIM_ABI_VERSION` on any breaking change.
   - Update the C# declarations in `UniverseSim/Assets/UniverseSim/Runtime/Native/` in the same change (including `CoreLibrary.ExpectedAbiVersion`).
9. **Determinism.**
   - No wall-clock time, randomness or unordered iteration in anything that affects simulation output or pack bytes.
   - Never enable fast-math or FMA contraction.

## C++ conventions

- Namespace `usim`, with one nested namespace per module (`usim::time`, `usim::frames`, ...). Public headers go in `core/include/usim/`, sources in `core/src/`.
- Naming:
  - Types are `PascalCase`.
  - Functions and variables are `snake_case`.
  - Constants are `k_snake_case`.
  - Private members have a trailing underscore (`value_`).
- Fallible operations return a `usim::Result<T>` (`usim/result.hpp`) rather than throwing. Mark pure query functions `[[nodiscard]]` and `noexcept` where it holds.
- `usim::ErrorCode` values equal the C ABI `USIM_STATUS_*` codes. A new code needs the `USIM_STATUS_` define, the static assertion in `usim_capi.cpp` and the C# `Status` value in the same change. Never renumber codes.
- Simulation time is `usim::time::Instant` (TDB, two-part). TT and TAI readings have their own types; UTC is only ever calendar fields (`DateTime`). Convert through `time::TimeScales`, never by adding offsets by hand.
- Tests never hand-type expected physical values. Use CSPICE results from `core/tests/fixtures/spice_reference.hpp` or `data/reference/` (regenerate with the commands below), or synthetic values that are obviously not real data.
- The data pack format is specified in `docs/formats/DATA_PACK.md` and implemented twice (Python writer, C++ reader in `usim::data`). Change the specification first, then both implementations and the round-trip fixture.
- No allocation in per-frame paths. Use `std::span` for caller-provided buffers.
- Warnings are errors. Fix the cause; do not suppress warnings without a comment explaining why.
- Format with `.clang-format`. Every public declaration gets a `///` doc comment that states units, frame and time scale where relevant.

## Working conventions

- **Plan upkeep:** when you finish a task, check it in `docs/IMPLEMENTATION_PLAN.md` and add a dated note of one or two lines (what was done, any deviation). Do not renumber tasks.
- **Decisions:** when a choice affects boundaries, formats, units, sources, dependencies or thresholds, write an ADR (`docs/adr/`, template in its README). Supersede old ADRs; never rewrite accepted ones.
- **Docs stay in sync with code.** If the implementation diverges from SPEC or ARCHITECTURE, update the document in the same change.
- **New data source:** add it to `docs/DATA_SOURCES.md` (with its tier) and `data/sources.json` before using it.
- **Ask the user** about tasks marked **(user)** in the plan and about product decisions the spec does not cover. Changes inside the Unity project (`UniverseSim/`) need no approval; the project owner has said anything there may be changed. Still keep the rules above (no physics in Unity, `.meta` files committed with their assets).
- **No CI yet** (P0-11 is deferred). Run the relevant commands below locally before declaring a task done.
- **Git:** commit only when the user asks. Never commit `Library/`, build output, raw data, packs or plugin binaries; the `.gitignore` files already exclude them. Binary assets go through Git LFS (`.gitattributes`).

## Unity specifics

- Unity **6000.5.1f1**, **URP 17.5**, **Input System**, **UI Toolkit** for UI. Assets use Force Text serialization with visible `.meta` files.
- The Unity project is the `UniverseSim/` folder. Project-owned content goes under `UniverseSim/Assets/UniverseSim/`.
- Always commit `.meta` files together with their assets. Never edit `Library/`, `Temp/`, `Logs/` or `UserSettings/`.
- The native core is copied to `Assets/UniverseSim/Plugins/<platform>/` by `tools/scripts/build-core-for-unity.sh` (`macOS/`, or `Windows/x86_64/` through `build-core-for-unity.ps1`). It is ignored by git, and its import settings are enforced by `Editor/NativePluginImportSettings.cs`. The plugin has its own preset (macOS 12 deployment target; Windows x64 Release), so run that script after core changes even when the other presets pass. On Windows, close the Editor first: Windows locks a loaded DLL.
- The main scene is built by `Editor/MainSceneBuilder.cs`; change the builder rather than editing `Main.unity` by hand.
- The Editor keeps a native plugin loaded after first use. **Restart the Editor after rebuilding the core.**
- Visual assets need an entry in `UniverseSim/Assets/UniverseSim/ATTRIBUTION.md`. The surface maps and that file are generated by `usim-pipeline textures` from `data/catalog/textures.json`; edit the catalog, not the outputs. Artistic assets (effect textures, artist's impressions, icons, UI art, models) go under `Assets/UniverseSim/Art/` and into `data/catalog/artistic-assets.json` with their author and license (an impression also with its body and references); `usim-pipeline attribution` then rewrites their section of `ATTRIBUTION.md` (A-04). An unlisted file there fails the tests.

## Commands

Development happens on Windows 11 x64 (since 2026-11-02; the Mac is no longer used, but the macOS build still works, see below). Run from the repository root. The core, plugin and Unity commands are PowerShell; the data pipeline and the `.sh` scripts run from Git Bash. Needs Visual Studio 2022 or newer with the C++ workload (it brings CMake and Ninja), Git for Windows with Git LFS, Unity 6000.5.1f1, and Python 3.12 or newer for the pipeline.

PowerShell:

```
# Once per PowerShell session: put the x64 MSVC toolchain, CMake and Ninja on PATH
. tools/scripts/enter-vs-dev-shell.ps1

# Core: configure, build, test (presets: dev, release; asan and unity-macos need Clang on macOS)
cd core; cmake --preset dev; cmake --build --preset dev; ctest --preset dev

# CLI host (queries against the default pack; see --help)
core\build\dev\apps\usim-cli\usim.exe --version
core\build\dev\apps\usim-cli\usim.exe state earth --center sun --time "2026-10-21T12:00:00 UTC"

# Validation tests only (the default pack against data/reference/; they also run with the full ctest)
cd core; ctest --preset dev -L validation

# Benchmarks (release builds only; NFR-03)
cd core; cmake --preset release; cmake --build --preset release; ctest --preset release -L benchmark

# Build the x64 plugin and copy it into Unity. Close the Editor first: Windows locks a loaded DLL.
powershell -ExecutionPolicy Bypass -File tools/scripts/build-core-for-unity.ps1

# Unity runs. Unity.exe returns at once when called directly, so start it with Start-Process -Wait. $unity is
# <Hub editor folder>\6000.5.1f1\Editor\Unity.exe (C:\Program Files\Unity\Hub\Editor by default; on the development
# machine D:\Program Files\Unity\6000.5.1f1\Editor\Unity.exe). Use absolute paths for -projectPath, -testResults and
# -logFile; without -logFile the log goes to %LOCALAPPDATA%\Unity\Editor\Editor.log.
$repo = (Get-Location).Path
function Run-Unity($arguments) { (Start-Process -FilePath $unity -ArgumentList $arguments -Wait -PassThru).ExitCode }

# EditMode tests, headless (the Editor must not have the project open)
Run-Unity @("-batchmode", "-nographics", "-projectPath", "$repo\UniverseSim", "-runTests", "-testPlatform", "EditMode",
            "-testResults", "$repo\editmode.xml", "-logFile", "$repo\editmode.log")

# PlayMode tests (they render: no -nographics; a full run takes over 10 minutes). $env:USIM_SCREENSHOT_DIR = "<dir>"
# also saves screenshots (a folder per visual style); -testFilter "<regex>" runs a subset.
Run-Unity @("-batchmode", "-projectPath", "$repo\UniverseSim", "-runTests", "-testPlatform", "PlayMode",
            "-testResults", "$repo\playmode.xml", "-logFile", "$repo\playmode.log")

# Rebuild the main scene from its assets (after changing the builder, materials or references)
Run-Unity @("-batchmode", "-nographics", "-quit", "-projectPath", "$repo\UniverseSim",
            "-executeMethod", "UniverseSim.Unity.Editor.MainSceneBuilder.Build", "-logFile", "$repo\scene.log")

# Windows player (packs copied into StreamingAssets first, see below) into UniverseSim\Builds\Windows (ignored), and
# its frame-rate measurement (A-13, NFR-11): a 1920 x 1080 window for about two and a half minutes, results as JSON
Run-Unity @("-batchmode", "-nographics", "-quit", "-projectPath", "$repo\UniverseSim",
            "-executeMethod", "UniverseSim.Unity.Editor.PlayerBuilder.BuildWindows", "-logFile", "$repo\player.log")
Start-Process "$repo\UniverseSim\Builds\Windows\UniverseSim.exe" -Wait -ArgumentList @("-screen-width", "1920",
    "-screen-height", "1080", "-screen-fullscreen", "0", "-usim-benchmark", "$repo\benchmark.json")
```

Native tools write to stderr; in Windows PowerShell 5.1, `2>&1` on them turns each line into an error record and can abort a script, so redirect through `cmd /c "<command> > <log> 2>&1"` when a log is needed.

Git Bash (data pipeline and scripts):

```
# Data pipeline: one-time setup of its virtual environment (packages pinned by hash), from any Python 3.12 or newer
<python> -m venv tools/pipeline/.venv
tools/pipeline/.venv/Scripts/python.exe -m pip install --require-hashes -r tools/pipeline/requirements.txt

# Download and pin sources, re-check them, build the packs, print a pack. If a publisher cannot be reached, fetch
# falls back to the mirrors a file lists in data/sources.json (pinned files only, which must match their pins;
# DATA_SOURCES.md section 2).
tools/pipeline/usim-pipeline fetch          # fetch --update re-pins after a publisher change (review first)
tools/pipeline/usim-pipeline verify
tools/pipeline/usim-pipeline build          # data/packs/solar-system-<version>.uspack, byte-reproducible
tools/pipeline/usim-pipeline info

# Optional moons pack (ADR-0010): satellite kernels and the Earth PCK (3.4 GB, optional sources), then the pack
# (325 MB, not committed)
tools/pipeline/usim-pipeline fetch --optional
tools/pipeline/usim-pipeline build --pack solar-system-moons

# Phase 6 kernels and mosaics into data/raw/ with progress and resume (an alternative to fetch --optional)
tools/scripts/download-phase6-kernels.sh --dry-run    # sizes and what is already here; --group to limit

# Surface maps (ADR-0014): download and pin their sources (about 4.8 GB, run on a fast connection), then
# reproject them into UniverseSim/Assets/UniverseSim/Textures/Bodies/ with ATTRIBUTION.md; rebuild the main
# scene afterwards so the host references them
tools/scripts/fetch-texture-sources.sh
tools/pipeline/usim-pipeline textures                 # the reprojection alone (offline, sources fetched)

# Artistic assets (A-04): check data/catalog/artistic-assets.json against Assets/UniverseSim/Art/ and rewrite their
# section of ATTRIBUTION.md (offline, without the surface maps' sources)
tools/pipeline/usim-pipeline attribution

# Sky cues (ADR-0018): validate data/catalog/sky-cues.json and write UniverseSim/Assets/UniverseSim/Data/SkyCues.json
# (offline; rebuild the main scene the first time so the host references it)
tools/pipeline/usim-pipeline sky-cues

# Copy the packs into StreamingAssets for player builds (the Editor also reads data/packs; the moons pack
# is preferred when present; --default-only skips it)
tools/scripts/copy-pack-to-unity.sh

# Validation with the pack fetched first (LFS checkout or pull, else build)
tools/scripts/run-validation.sh

# Regenerate reference data: unit-fixtures, test-pack, spice (all three: offline), horizons (network)
tools/pipeline/usim-pipeline reference offline

# Pipeline tests
tools/pipeline/.venv/Scripts/python.exe -m pytest tools/pipeline
```

The pipeline tests pass on Windows, and the default pack rebuilt there is byte for byte the committed one (2026-11-02). Unit fixtures generated by CSPICE on macOS differ from Windows output by 1 or 2 ulp; the comparison allows 4 ulp (recorded in the plan, P0-13).

macOS (supported, no longer the development machine): the same steps with `core/build/dev/apps/usim-cli/usim`, `tools/scripts/build-core-for-unity.sh` (universal arm64 and x86_64 plugin, preset `unity-macos`), the `asan` preset (AddressSanitizer and UBSan, not available with MSVC), the Editor at `/Applications/Unity/Hub/Editor/6000.5.1f1/Unity.app/Contents/MacOS/Unity` (it waits when called directly), and the pipeline's `.venv/bin/` layout.

Data rules for agents:
- The default pack in `data/packs/` is committed through Git LFS (ADR-0006). The optional moons pack (`solar-system-moons-*.uspack`) is never committed (ADR-0010); pack lookups must match the default pack as `solar-system-<digit>...`, not `solar-system-*`. After a change that alters pack bytes (pipeline, sources, catalog selection), rebuild it, run the tests, and bump `version` in `data/catalog/packs.json` if the content changed; remove the old pack file in the same change.
- After changing the pack writer, regenerate the round-trip fixture (`reference test-pack`). Never edit generated files in `core/tests/fixtures/` or `data/reference/` by hand.
- Kernel values are the correctly rounded text of the kernel, cross-checked against CSPICE (ADR-0007). Do not switch to raw `gdpool` values.

## Writing style for documents

- No em dashes. Use hyphens, colons or separate sentences.
- No emojis in documentation.
- Keep documents clear and professional. Prefer tables and short sections that agents can scan.
