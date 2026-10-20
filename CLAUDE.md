# CLAUDE.md

Guidance for AI agents (and humans) working in this repository.

## What this project is

A universe simulation built on real, verified data. It starts with the Solar System and expands outward. The simulation is an engine-agnostic C# library. Unity is only the rendering layer. Read the documents below before making changes.

## Read first

1. [docs/SPEC.md](docs/SPEC.md): requirements, conventions (units, time, frames, IDs), accuracy targets.
2. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): modules, the host contract, repository layout.
3. [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md): phases and tasks. Find the current phase and the next unchecked task.
4. [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md): the registry of allowed data sources and their tiers.
5. [docs/adr/](docs/adr/): why the key decisions were made.

## Non-negotiable rules

1. **The engine boundary.** Nothing under `core/` or `tools/` references `UnityEngine` or any other engine. Unity code under `UniverseSim/` never does orbital mechanics, time scale conversion, frame math or ephemeris evaluation. If the renderer needs a value, add it to the core API.
2. **Real data only.** Never type a physical value (mass, GM, radius, orbital element, rotation rate, albedo, and so on) into code or data from memory. Values enter through the data pipeline from a source registered in `docs/DATA_SOURCES.md` and `data/sources.json`. The only exceptions are definitional constants (for example the au, the speed of light, J2000.0, TT - TAI) in the core's `Constants` class, each with a citation. If a value cannot be sourced, leave it missing and say so. Do not estimate.
3. **Provenance travels with the data.** Every catalog value carries its source reference. Derived values record their derivation. Do not strip provenance for convenience.
4. **Units, frames and time are explicit.** Use km, s, km/s, rad, GM in km^3/s^2, TDB internally and the ICRF as the native frame. Put units in member names or XML docs. Convert only at the boundaries (presentation and user input).
5. **Precision.** Use `double` in the core. Single precision exists only in host code, after re-centering on the view origin. Axis and handedness mapping goes through `Presentation.AxisConvention`, never by hand in host code.
6. **Validate numerical work.** Numerical changes come with tests. Validation tests (`Category=Validation`) must pass. Never loosen a threshold without recording the measured cause in the plan or an ADR.
7. **Zero runtime dependencies in the core.** No NuGet packages in `UniverseSim.Core`. Tools and tests may use packages.
8. **Determinism.** No wall-clock time, randomness or unordered iteration in anything that affects simulation output or pack bytes.

## Working conventions

- **Plan upkeep:** when you finish a task, check it in `docs/IMPLEMENTATION_PLAN.md` and add a dated note of one or two lines (what was done, any deviation). Do not renumber tasks.
- **Decisions:** when a choice affects boundaries, formats, units, sources, dependencies or thresholds, write an ADR (`docs/adr/`, template in its README). Supersede old ADRs; never rewrite them.
- **Docs stay in sync with code.** If the implementation diverges from SPEC or ARCHITECTURE, update the document in the same change.
- **New data source:** add it to `docs/DATA_SOURCES.md` (with its tier) and `data/sources.json` before using it.
- **Ask the user** about tasks marked **(user)** in the plan, and before deleting Unity template content or making product decisions not covered by the spec.
- **Git:** commit only when the user asks. Never commit `Library/`, raw data, packs or built DLLs (the `.gitignore` files already exclude them).

## Unity specifics

- Unity **6000.5.1f1**, **URP 17.5**, **Input System**, **UI Toolkit** for UI. Assets use Force Text serialization with visible `.meta` files.
- The Unity project is the `UniverseSim/` folder. Project-owned content goes under `UniverseSim/Assets/UniverseSim/`.
- Always commit `.meta` files together with their assets. Never edit `Library/`, `Temp/`, `Logs/` or `UserSettings/`.
- The core arrives as a prebuilt DLL in `Assets/Plugins/UniverseSim.Core/` (see ADR-0002). Rebuild and copy it after changing the core.
- Visual assets need an entry in `UniverseSim/Assets/UniverseSim/ATTRIBUTION.md`.

## Commands

To be filled in during Phase 0 (task P0-12), once the solution exists. Planned:

```
dotnet build core/UniverseSim.sln
dotnet test  core/UniverseSim.sln
dotnet run --project tools/UniverseSim.DataPipeline -- fetch
dotnet run --project tools/UniverseSim.DataPipeline -- build
tools/scripts/build-core-for-unity.sh
```

## Writing style for documents

- No em dashes. Use hyphens, colons or separate sentences.
- No emojis in documentation.
- Keep documents clear and professional. Prefer tables and short sections that agents can scan.
