# Universe Simulation

A physically accurate simulation of the universe, built on real data from verified scientific sources. It starts with the Solar System and expands outward.

- **Real data:** positions come from the JPL DE440 planetary ephemeris, and physical constants from NAIF and IAU sources. Every value is traceable to its source.
- **Engine-agnostic core:** all calculations live in a standalone C++20 library (`core/`) with no engine dependencies and a stable C ABI.
- **Unity renders:** the Unity project is a thin host that displays what the core computes. Other engines can be added later.

## Status

Phases 0 to 5 are complete on macOS: the engine-agnostic core and the Unity viewer (v1). A reproducible Python pipeline builds the data pack from pinned NAIF kernels. The core evaluates the DE440 ephemeris (within 1 mm of NASA's CSPICE toolkit and within 10 m of JPL Horizons), orients bodies with the IAU rotation models, runs the simulation clock, samples orbit paths and fills per-frame snapshots in a few microseconds. It is exposed through a C ABI and the `usim` command line. The Unity viewer shows the Solar System at the current time with time controls, focus and zoom from 1 km above a surface to the whole system, orbit lines, labels, an info panel with sources, and a scale mode switch. Phase 6 adds the 9,096 naked-eye stars of the Bright Star Catalogue as the sky, Saturn's rings, and, in an optional pack built from the JPL satellite ephemerides, the planet centers and 21 major moons from 2000 to 2100. Textures are still to come. See the [implementation plan](docs/IMPLEMENTATION_PLAN.md).

## Documentation

| Document | Contents |
|---|---|
| [docs/SPEC.md](docs/SPEC.md) | Requirements, conventions and accuracy targets |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Modules, C ABI, host contract and repository layout |
| [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | Phases, tasks and status |
| [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md) | Registry of verified data sources |
| [docs/API.md](docs/API.md) | Guide for host authors: frame loop, precision, time, bindings |
| [docs/formats/DATA_PACK.md](docs/formats/DATA_PACK.md) | The data pack file format |
| [docs/adr/](docs/adr/) | Architecture decision records |
| [CLAUDE.md](CLAUDE.md) | Rules and conventions for AI agents and contributors |

## Repository layout

```
docs/          specification, architecture, plan, data sources, ADRs
core/          C++20 simulation library, C ABI, CLI host and tests (CMake)
tools/         scripts (build-core-for-unity, run-validation); Python data pipeline (tools/pipeline)
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

# 3. Open UniverseSim/ in Unity 6000.5.1f1, open Assets/UniverseSim/Scenes/Main.unity and press Play.
#    Drag to orbit, scroll to zoom, click a body to select it, double-click (or Enter) to focus it,
#    comma and period to step through bodies, F to fly (WASD, Q/E, Shift), Space to pause,
#    [ and ] to change the rate.
```

Query the simulation from the command line (JSON by default, `--format csv` for tables):

```
core/build/dev/apps/usim-cli/usim state earth --center sun --time "2026-10-21T12:00:00 UTC"
core/build/dev/apps/usim-cli/usim snapshot --center earth --km-per-unit 1e6 --format csv
core/build/dev/apps/usim-cli/usim orbit mars
core/build/dev/apps/usim-cli/usim --help
```

In the default pack, Mars to Pluto are shown at their system barycenters and flagged approximate, because the default ephemeris file has no planet-center data for them ([ADR-0008](docs/adr/0008-planet-positions-at-system-barycenters.md)). The optional moons pack has the real centers and the moons; the viewer uses it when it has been built ([ADR-0010](docs/adr/0010-moons-in-an-optional-pack.md)).

The default data pack (`data/packs/solar-system-1.2.0.uspack`) is committed through Git LFS, so a clone has data without running the pipeline. To rebuild data from the sources:

```
python3 -m venv tools/pipeline/.venv
tools/pipeline/.venv/bin/pip install --require-hashes -r tools/pipeline/requirements.txt
tools/pipeline/usim-pipeline fetch     # download and verify the pinned NAIF kernels
tools/pipeline/usim-pipeline build     # rebuild the pack (byte-identical to the committed one)
tools/pipeline/usim-pipeline info      # list bodies, values with sources, and coverage

# The optional moons pack (downloads 3.4 GB of satellite kernels; about 300 MB pack, 2000 to 2100)
tools/pipeline/usim-pipeline fetch --optional
tools/pipeline/usim-pipeline build --pack solar-system-moons
```

The plugin build currently targets macOS. A Windows build is deferred (P0-13); it can be cross-compiled on macOS when needed.

## Data acknowledgments

This project uses data from NASA/JPL Solar System Dynamics and the NAIF SPICE generic kernels, including the DE440 planetary and lunar ephemeris (Park, R. S., Folkner, W. M., Williams, J. G., Boggs, D. H., 2021, AJ 161:105) the JPL satellite ephemerides, and the IAU WGCCRE 2015 rotational elements (Archinal, B. A., et al., 2018, CMDA 130:22). The stars come from the Bright Star Catalogue, 5th revised edition (Hoffleit, D., Warren Jr., W. H., 1991), retrieved through the VizieR service of CDS, Strasbourg. Saturn's ring dimensions come from the IAU WGPSN Gazetteer of Planetary Nomenclature and their optical depths from the NASA PDS Ring-Moon Systems Node. The full list is in [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md).

## License

Not yet chosen (see open question OQ-05 in the spec).
