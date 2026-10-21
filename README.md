# Universe Simulation

A physically accurate simulation of the universe, built on real data from verified scientific sources. It starts with the Solar System and expands outward.

- **Real data:** positions come from the JPL DE440 planetary ephemeris, and physical constants from NAIF and IAU sources. Every value is traceable to its source.
- **Engine-agnostic core:** all calculations live in a standalone C++20 library (`core/`) with no engine dependencies and a stable C ABI.
- **Unity renders:** the Unity project is a thin host that displays what the core computes. Other engines can be added later.

## Status

Phases 0 to 2 are complete on macOS. The core has its foundations (math, time scales and calendars, reference frames, the body catalog model, host axis conventions and the error model), checked against NASA's CSPICE toolkit, and Unity loads it as a native plugin. A reproducible Python pipeline builds the data pack from pinned NAIF kernels, the core reads and validates it, and reference data for validation is in `data/reference/`. Phase 3 (the ephemeris engine) is next. See the [implementation plan](docs/IMPLEMENTATION_PLAN.md).

## Documentation

| Document | Contents |
|---|---|
| [docs/SPEC.md](docs/SPEC.md) | Requirements, conventions and accuracy targets |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Modules, C ABI, host contract and repository layout |
| [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | Phases, tasks and status |
| [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md) | Registry of verified data sources |
| [docs/formats/DATA_PACK.md](docs/formats/DATA_PACK.md) | The data pack file format |
| [docs/adr/](docs/adr/) | Architecture decision records |
| [CLAUDE.md](CLAUDE.md) | Rules and conventions for AI agents and contributors |

## Repository layout

```
docs/          specification, architecture, plan, data sources, ADRs
core/          C++20 simulation library, C ABI, CLI host and tests (CMake)
tools/         scripts (build-core-for-unity); Python data pipeline (tools/pipeline)
data/          source registry, lock file, catalog inputs, reference vectors, the default pack (Git LFS)
UniverseSim/   Unity 6 project (rendering layer)
```

## Prerequisites

- Git with Git LFS (`git lfs install` once per machine)
- CMake 3.25 or newer, Ninja, and a C++20 compiler (Apple Clang from Xcode on macOS)
- Unity 6000.5.1f1 (URP)
- Python 3.12 or newer, only to rebuild data or reference files. The repository pins 3.12.8 in `.python-version` for pyenv users.

On macOS with Homebrew: `brew install git-lfs cmake ninja`.

## Getting started

```
# 1. Build and test the core
cd core
cmake --preset dev
cmake --build --preset dev
ctest --preset dev
cd ..

# 2. Build the native plugin and copy it into the Unity project
tools/scripts/build-core-for-unity.sh

# 3. Open UniverseSim/ in Unity 6000.5.1f1 and open Assets/UniverseSim/Scenes/Main.unity.
#    Entering Play mode logs the core version.
```

The default data pack (`data/packs/solar-system-1.0.0.uspack`) is committed through Git LFS, so a clone has data without running the pipeline. To rebuild data from the sources:

```
python3 -m venv tools/pipeline/.venv
tools/pipeline/.venv/bin/pip install --require-hashes -r tools/pipeline/requirements.txt
tools/pipeline/usim-pipeline fetch     # download and verify the pinned NAIF kernels
tools/pipeline/usim-pipeline build     # rebuild the pack (byte-identical to the committed one)
tools/pipeline/usim-pipeline info      # list bodies, values with sources, and coverage
```

The plugin build currently targets macOS. A Windows build is deferred (P0-13); it can be cross-compiled on macOS when needed.

## Data acknowledgments

This project uses data from NASA/JPL Solar System Dynamics and the NAIF SPICE generic kernels, including the DE440 planetary and lunar ephemeris (Park, R. S., Folkner, W. M., Williams, J. G., Boggs, D. H., 2021, AJ 161:105) and the IAU WGCCRE 2015 rotational elements (Archinal, B. A., et al., 2018, CMDA 130:22). The full list is in [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md).

## License

Not yet chosen (see open question OQ-05 in the spec).
