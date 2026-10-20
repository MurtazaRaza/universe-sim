# Universe Simulation

A physically accurate simulation of the universe, built on real data from verified scientific sources. It starts with the Solar System and expands outward.

- **Real data:** positions come from the JPL DE440 planetary ephemeris, and physical constants from NAIF and IAU sources. Every value is traceable to its source.
- **Engine-agnostic core:** all calculations live in a standalone C# library (`UniverseSim.Core`) with no engine dependencies.
- **Unity renders:** the Unity project is a thin host that displays what the core computes. Other engines can be added later.

## Status

Planning and foundation (Phase 0). See the [implementation plan](docs/IMPLEMENTATION_PLAN.md).

## Documentation

| Document | Contents |
|---|---|
| [docs/SPEC.md](docs/SPEC.md) | Requirements, conventions and accuracy targets |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Modules, host contract and repository layout |
| [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | Phases, tasks and status |
| [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md) | Registry of verified data sources |
| [docs/adr/](docs/adr/) | Architecture decision records |
| [CLAUDE.md](CLAUDE.md) | Rules and conventions for AI agents and contributors |

## Repository layout

```
docs/          specification, architecture, plan, data sources, ADRs
core/          engine-agnostic simulation library and tests (planned)
tools/         data pipeline, CLI host, reference generators, scripts (planned)
data/          source registry, lock file, catalog inputs, reference vectors (planned)
UniverseSim/   Unity 6 project (rendering layer)
```

## Prerequisites

- Unity 6000.5.1f1 (URP)
- .NET 10 SDK (for the core, the tools and the tests)
- Python 3 with SpiceyPy (only for regenerating validation reference data)

## Getting started

To be written once Phase 0 is complete (task P0-12). The planned flow: build the core, fetch and build the data pack, copy both into the Unity project, then open `UniverseSim/` in Unity.

## Data acknowledgments

This project uses data from NASA/JPL Solar System Dynamics and the NAIF SPICE generic kernels, including the DE440 planetary and lunar ephemeris (Park, R. S., Folkner, W. M., Williams, J. G., Boggs, D. H., 2021, AJ 161:105) and the IAU WGCCRE 2015 rotational elements (Archinal, B. A., et al., 2018, CMDA 130:22). The full list is in [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md).

## License

Not yet chosen (see open question OQ-05 in the spec).
