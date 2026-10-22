# ADR-0009: Depth and precision across scales

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-21 |

## Context

P5-05 asks how the viewer shows a planet's surface and the outer Solar System in one view without precision artifacts (SPEC FR-REN-01, NFR-02). Distances span from about a kilometer to billions of kilometers. Unity stores transforms in single precision (about 7 significant digits) and warns about coordinates far from the origin; depth buffers have limited resolution; and the near and far planes of one camera bound what it can draw. The floating origin (ARCHITECTURE section 5.2) already keeps the camera at the origin, but a body billions of kilometers away still gets coordinates of order 1e9 to 1e10 render units when one unit is about a kilometer, as it must be near a surface.

ARCHITECTURE section 5.2 listed three options for depth: the reversed-Z buffer, dynamic near and far planes, and layered cameras (a scaled-space camera for distant objects plus a local one).

## Decision

1. **Render units follow the camera.** The camera rig sets one render unit to the camera's altitude above its focus body, so the nearest surface is about one unit away at any zoom.
2. **The core compresses far distances** (new `View::compression_distance`, `presentation::distance_compression`). Beyond a horizon (1e4 render units in the viewer), a point at render distance d from the camera is moved along its line of sight to c (1 + ln(d / c)), and the body's radii are scaled by the same factor. Scaling about the camera keeps every direction and apparent size, so the image is unchanged; the compressed distance grows monotonically, so depth order is kept; and no coordinate exceeds about 40 times the horizon. Orbit paths are compressed the same way. Snapshots flag compressed bodies (`snapshot_compressed`).
3. **Lighting does not depend on positions in render space.** Each snapshot body carries the unit vector toward the star, computed by the core from the true positions (`light_direction`), and the body shader lights from it. Phases stay correct for compressed bodies.
4. **One camera with fixed planes**: near 0.02 render units (2 % of the altitude), far 60 times the horizon. Unity uses a reversed-Z depth buffer on Metal, Direct3D 11 and later, and Vulkan; with compression the far/near ratio stays near 3e7, within its resolution.
5. Layered cameras are not used.

## Consequences

- The PlayMode tests check the result: from 1 km above the Earth the Moon, and from near Pluto the Sun, move across the sky without jitter (each frame's direction stays within 1e-5 rad of the midpoint of its neighbours; a pixel is about 8e-4 rad), and body positions in Unity match the usim CLI after the documented mapping.
- The camera's minimum altitude is 1 km: below that, float rounding of a rotating planet mesh (whose center is thousands of units away) becomes a visible fraction of the altitude.
- Hosts get the same behavior from the core in any engine; the compression math lives in `usim::presentation`, not in the host.
- Distances measured in render space are not true distances beyond the horizon. Hosts must take distances from the core (states), never from render positions; the info panel does.
- Line widths of compressed orbits are set from the distance to the orbit's center; a screen-space line shader would make them uniform (later).

## Alternatives considered

- **Layered cameras (scaled space)**: a common technique, but it needs two scene representations, careful sorting between layers, and duplicated objects at the boundary. Compression gives the same image with one camera and keeps the logic in the core.
- **Dynamic near and far planes only**: cannot keep both a nearby surface and a planet 1e10 units away within float transform range.
- **Logarithmic depth written in the shader**: fixes depth resolution but not the size of the coordinates themselves, and it disables early depth tests.
