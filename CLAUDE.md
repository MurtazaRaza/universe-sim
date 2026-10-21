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
- The native core is copied to `Assets/UniverseSim/Plugins/<platform>/` by `tools/scripts/build-core-for-unity.sh`. It is ignored by git, and its import settings are enforced by `Editor/NativePluginImportSettings.cs`.
- The Editor keeps a native plugin loaded after first use. **Restart the Editor after rebuilding the core.**
- Visual assets need an entry in `UniverseSim/Assets/UniverseSim/ATTRIBUTION.md`.

## Commands

Run from the repository root unless noted.

```
# Core: configure, build, test (presets: dev, release, asan, unity-macos)
cd core && cmake --preset dev && cmake --build --preset dev && ctest --preset dev

# Same under AddressSanitizer + UBSan
cd core && cmake --preset asan && cmake --build --preset asan && ctest --preset asan

# CLI host
core/build/dev/apps/usim-cli/usim --version

# Build the native plugin and copy it into Unity (macOS: universal arm64 + x86_64)
tools/scripts/build-core-for-unity.sh

# Unity EditMode tests, headless (the Editor must not have the project open)
/Applications/Unity/Hub/Editor/6000.5.1f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics \
  -projectPath UniverseSim -runTests -testPlatform EditMode -testResults /tmp/editmode.xml

# Validation tests only (the default pack against data/reference/; they also run with the full ctest)
cd core && ctest --preset dev -L validation

# Data pipeline: one-time setup of its virtual environment (packages pinned by hash)
python3 -m venv tools/pipeline/.venv
tools/pipeline/.venv/bin/pip install --require-hashes -r tools/pipeline/requirements.txt

# Download and pin sources, re-check them, build the packs, print a pack
tools/pipeline/usim-pipeline fetch          # fetch --update re-pins after a publisher change (review first)
tools/pipeline/usim-pipeline verify
tools/pipeline/usim-pipeline build          # data/packs/solar-system-<version>.uspack, byte-reproducible
tools/pipeline/usim-pipeline info

# Regenerate reference data: unit-fixtures, test-pack, spice (all three: offline), horizons (network)
tools/pipeline/usim-pipeline reference offline

# Pipeline tests
tools/pipeline/.venv/bin/python -m pytest tools/pipeline
```

Python: the repository pins 3.12.8 in `.python-version` (pyenv), so `python3` at the repo root is 3.12.8. On this machine that interpreter prints harmless "blake2" import errors (Homebrew `libb2` is missing); SHA-256 and MD5 work.

Data rules for agents:
- The default pack in `data/packs/` is committed through Git LFS (ADR-0006). After a change that alters pack bytes (pipeline, sources, catalog selection), rebuild it, run the tests, and bump `version` in `data/catalog/packs.json` if the content changed; remove the old pack file in the same change.
- After changing the pack writer, regenerate the round-trip fixture (`reference test-pack`). Never edit generated files in `core/tests/fixtures/` or `data/reference/` by hand.
- Kernel values are the correctly rounded text of the kernel, cross-checked against CSPICE (ADR-0007). Do not switch to raw `gdpool` values.

## Writing style for documents

- No em dashes. Use hyphens, colons or separate sentences.
- No emojis in documentation.
- Keep documents clear and professional. Prefer tables and short sections that agents can scan.
