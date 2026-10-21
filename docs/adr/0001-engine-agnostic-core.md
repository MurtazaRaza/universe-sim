# ADR-0001: Engine-agnostic simulation core with thin rendering hosts

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-20 |

## Context

The project simulates the universe from real data, starting with the Solar System. Unity is the first renderer, but the project must be able to move to (or add) another engine later. If simulation logic leaks into Unity scripts, a port means rewriting and revalidating the physics, which is the most expensive and error-prone part of the project.

Unity also brings constraints that do not suit astronomy: single-precision transforms, a left-handed coordinate system and a frame-driven update loop.

## Decision

1. All simulation logic lives in a standalone core library in `core/`. That includes time, frames, ephemerides, propagation, the catalog, the simulation clock and the engine-agnostic presentation math (view origin re-centering, scale modes, axis conventions).
2. The core has no references to any engine and no third-party runtime dependencies.
3. Renderers are **hosts**. A host loads a data pack into the core, advances the clock, reads snapshots and maps them to its scene. It never computes or modifies physical state.
4. A headless CLI host exists from the start. It proves independence from any engine and serves tests and scripting.
5. The data pack format is documented, so ports in any language can reuse the data.
6. The Unity project stays in this repository (in `UniverseSim/`) for now, as one host among potentially several.

## Consequences

- The core can be tested quickly with `ctest`, with no Unity in the loop.
- Changing renderers means writing a new host, not new physics.
- Some work is duplicated at the boundary (for example the axis mapping and the conversion to float). This is kept small by putting the math in the core's `Presentation` namespace.
- Unity code has to wait for core APIs. When the renderer needs a number, the core must expose it first. This is intentional.

## Alternatives considered

- **Simulation inside Unity (MonoBehaviours, Jobs, DOTS).** The fastest start, but it ties the physics to Unity, its float precision and its update loop. Rejected because it conflicts with the portability goal.
- **Simulation as a separate process (local server, IPC).** Maximally decoupled, but adds latency, deployment complexity and serialization costs for little benefit at this scale. It can still be built later on top of the same core.
