# Universe Simulation

A physically accurate simulation of the universe, built on real data from verified scientific sources. It starts with the Solar System and expands outward.

- **Real data:** positions come from the JPL DE440 planetary ephemeris, and physical constants from NAIF and IAU sources. Every value is traceable to its source.
- **Engine-agnostic core:** all calculations live in a standalone C++20 library (`core/`) with no engine dependencies and a stable C ABI.
- **Unity renders:** the Unity project is a thin host that displays what the core computes. Other engines can be added later.

## Status

Phase 0 (foundation) is complete on macOS: the core builds and passes its tests, and Unity loads it as a native plugin. See the [implementation plan](docs/IMPLEMENTATION_PLAN.md).

## Documentation

| Document | Contents |
|---|---|
| [docs/SPEC.md](docs/SPEC.md) | Requirements, conventions and accuracy targets |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Modules, C ABI, host contract and repository layout |
| [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | Phases, tasks and status |
| [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md) | Registry of verified data sources |
| [docs/adr/](docs/adr/) | Architecture decision records |
| [CLAUDE.md](CLAUDE.md) | Rules and conventions for AI agents and contributors |

## Repository layout

```
docs/          specification, architecture, plan, data sources, ADRs
core/          C++20 simulation library, C ABI, CLI host and tests (CMake)
tools/         scripts (build-core-for-unity); Python data pipeline (planned)
data/          source registry, lock file, catalog inputs, reference vectors (planned)
UniverseSim/   Unity 6 project (rendering layer)
```

## Prerequisites

- Git with Git LFS (`git lfs install` once per machine)
- CMake 3.25 or newer, Ninja, and a C++20 compiler (Apple Clang from Xcode on macOS)
- Unity 6000.5.1f1 (URP)
- Python 3.12 or newer (from Phase 2, for the data pipeline). The repository pins 3.12.8 in `.python-version` for pyenv users.

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

The plugin build currently targets macOS. A Windows build is deferred (P0-13); it can be cross-compiled on macOS when needed.

## Data acknowledgments

This project uses data from NASA/JPL Solar System Dynamics and the NAIF SPICE generic kernels, including the DE440 planetary and lunar ephemeris (Park, R. S., Folkner, W. M., Williams, J. G., Boggs, D. H., 2021, AJ 161:105) and the IAU WGCCRE 2015 rotational elements (Archinal, B. A., et al., 2018, CMDA 130:22). The full list is in [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md).

## License

Not yet chosen (see open question OQ-05 in the spec).
