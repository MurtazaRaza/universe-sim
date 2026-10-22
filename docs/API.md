# Host API Guide

| | |
|---|---|
| Status | Draft v0.2 (C ABI version 3) |
| Last updated | 2026-10-22 |
| Related | [ARCHITECTURE.md](ARCHITECTURE.md) (sections 3.4, 3.5 and 5), [SPEC.md](SPEC.md), `core/capi/include/usim/usim.h` |

This guide is for anyone writing a host: a program that shows or uses the simulation (the Unity project, a future Godot or Unreal host, a script). The header `usim.h` is the reference for every function; this guide explains how the pieces fit together.

## 1. Which API to use

| Host | API | Notes |
|---|---|---|
| C++ (Unreal, tools, tests) | `usim::sim::Simulation` (`core/include/usim/sim/simulation.hpp`), linked statically as `usim::core` | Same functions as the C ABI, with `Result<T>` errors |
| Any other language (C#, Python, Rust, GDScript through GDExtension) | The C ABI in the shared library `universesim` (`usim.h`) | Plain C types only; the Unity binding is in `UniverseSim/Assets/UniverseSim/Runtime/Native/` |
| Scripts and checks | The `usim` command line (`core/build/<preset>/apps/usim-cli/usim --help`) | JSON or CSV; a reference to compare a host against |

The host never does orbital mechanics, time scale conversion, frame math or ephemeris evaluation (ARCHITECTURE section 5). It asks the core and draws what comes back.

## 2. Loading

```c
usim_simulation* sim = NULL;
int32_t status = usim_simulation_load_file("data/packs/solar-system-1.2.0.uspack", &sim);
if (status != USIM_STATUS_OK) {
    log("cannot load the pack: %s (%s)", usim_last_error_message(), usim_status_name(status));
}
...
usim_simulation_destroy(sim);
```

- Check `usim_abi_version()` against the version your binding was written for before anything else, and compare your struct sizes with `usim_struct_size()` (section 9).
- A file of a few hundred bytes starting with `version https://git-lfs` is a Git LFS pointer, not a pack; the loader says so (`git lfs pull`).
- Loading reads and verifies the whole pack (SHA-256 included). It takes a fraction of a second for the default pack in a release build, and about 2.6 s for the optional moons pack (304 MB, [ADR-0010](adr/0010-moons-in-an-optional-pack.md)); do it once, not per scene change.
- `usim_simulation_load_memory()` loads from a buffer, for platforms where files come from an archive.

## 3. The frame loop

```c
// Once:
usim_snapshot_request request = {0};
request.center = focus_body;                      // the body the camera looks at
request.frame = (usim_frame){USIM_FRAME_ECLIPJ2000, 0, {0, 0, 0}};
request.km_per_render_unit = 1.0e6;               // pick for your scene (section 4)
request.axes = USIM_AXES_UNITY;                   // your engine's convention (section 6)
request.scale_mode = USIM_SCALE_MODE_TRUE;
request.body_scale = 1.0;
request.compression_distance = 1.0e4;             // section 4
usim_body_snapshot bodies[64];                    // capacity >= usim_body_count()

// Every frame:
usim_clock_advance(sim, real_seconds_since_last_frame);
usim_axes_from_host(USIM_AXES_UNITY, camera_offset_host_km, &request.origin_km);  // never swap axes yourself
request.km_per_render_unit = camera_altitude_km;  // the nearest surface is about one unit away
int32_t count = 0;
usim_fill_snapshot(sim, &request, bodies, 64, &count);
for (int32_t i = 0; i < count; ++i) {
    if (bodies[i].flags & USIM_SNAPSHOT_NO_POSITION) { hide(i); continue; }
    place(i, (float)bodies[i].render_position.x, ...);   // relative to the camera, so floats are fine
    rotate(i, bodies[i].orientation);                    // already in host axes
    scale(i, bodies[i].render_radii);                    // per local axis, for a unit sphere mesh
    light(i, bodies[i].light_direction);                 // toward the star, from the true positions
    if (bodies[i].flags & USIM_SNAPSHOT_APPROXIMATE) { mark_approximate(i); }
}
```

- `usim_fill_snapshot` allocates nothing and takes a few microseconds for the v1 bodies (NFR-03); call it every frame.
- Bodies come in catalog order, the same as `usim_body_info_at`, so index `i` is the same body every frame.
- The camera stays at the render origin. When the user changes focus, change `request.center` and `request.origin_km`; nothing else moves.
- Light each body from `light_direction` rather than from a scene light: it is correct at every scale, including for compressed bodies (section 4), and gives correct phases.
- In `USIM_SCALE_MODE_ENLARGED`, planets, dwarf planets and moons grow by `body_scale`, capped at 40 % of a planet-moon distance; the star keeps its true size.

### Orbit lines

```c
usim_body_id around;
usim_default_orbit_center(sim, body, &around);   // the Sun, or a moon's planet
int32_t n = 0; uint32_t flags = 0;
usim_orbit_path(sim, body, around, &request, points, capacity, &n, &flags);   // render positions
```

The simulation caches one revolution per body and samples it again only after a large time jump (about 2 % of a revolution), so calling this every frame for the visible orbits is cheap. `*out_count` reports the full point count; if it exceeds `capacity`, grow the buffer. Points are render positions for the same view as the snapshot, even when the orbit is drawn around a body other than `request.center`. `usim_orbit_path` updates the cache, so it needs exclusive access to the simulation (section 8).

### Stars and rings (pack format 1.2, ABI version 3)

```c
int32_t star_count = 0;
usim_star_count(sim, &star_count);                         // 0 without a star catalog
usim_star* stars = malloc(sizeof(usim_star) * star_count); // once
usim_fill_stars(sim, &request, stars, star_count, &written);   // only frame and axes of the request apply
for each star: draw a point at camera_position + direction * sky_distance, sized and colored from
               v_magnitude and b_minus_v (USIM_STAR_HAS_COLOR) by your own display mapping

int32_t ring_count = 0;
usim_ring_count(sim, &ring_count);
usim_ring ring; usim_ring_at(sim, i, &ring);  // body_index, inner and outer radius (km), optical depth
                                              // range and the sources of both
```

- Star directions are unit vectors in the display frame and the host's axes at the clock's time, with proper motion applied. Stars are infinitely far away: draw them around the camera, behind everything. The directions change slowly; the Unity host fills them again after a simulated year.
- Brightness and color are yours to map; the core gives the catalog's V magnitude and B-V color index. Say in your code that the mapping is a display choice.
- A ring lies in its body's equatorial plane: the body-fixed XY plane, which the snapshot's `orientation` maps to the display. Scale ring radii in km by the body's `render_radius / mean_radius_km` so they follow the scale mode and distance compression like the body. Show `optical_depth_source_index`'s tier: Saturn's optical depths come from a Tier 2 source.

## 4. Precision: the floating origin

Distances in the Solar System span from meters to billions of kilometers. A single-precision float resolves about 7 digits, so absolute positions in floats would shake visibly far from the origin (SPEC NFR-02).

The core therefore does all position math in double precision and hands the host only small numbers:

1. Positions are computed relative to `request.center` (usually the focus body).
2. `request.origin_km`, the camera's offset from that body, is subtracted.
3. The result is divided by `km_per_render_unit` and mapped to the host's axes.

Values near the camera are then small, and the host casts them to float without visible loss. Keep the camera position in double precision on the host side (as the offset from the focus), never as a float world position.

Choose `km_per_render_unit` so that what the camera shows falls in a comfortable range for your engine's depth buffer; it can change every frame. The Unity host uses the camera's altitude above its focus, so the nearest surface is about one unit away.

Far bodies would still get very large coordinates (the Sun is about 1e8 units away from a camera 1 km above the Earth). Set `request.compression_distance`: beyond it, the core pulls each body toward the camera along its line of sight and shrinks it by the same factor, logarithmically. The image seen from the camera is unchanged (directions and apparent sizes are kept, and depth order too), and no coordinate exceeds about 40 times the horizon, so one camera with fixed near and far planes covers everything ([ADR-0009](adr/0009-depth-and-precision-across-scales.md)). Render-space distances beyond the horizon are therefore not true distances: take distances from `usim_state_at`.

## 5. Time

- The simulation's time is **TDB** (`usim_instant`: whole days since J2000.0 plus seconds of day). Seconds past J2000 equal SPICE ephemeris time.
- The core reads no wall clock (determinism, NFR-04). To show "now", read the host's UTC clock and call `usim_time_from_fields(sim, USIM_SCALE_UTC, year, month, day, hour, minute, second, &t)`, then `usim_clock_set_time`.
- UTC exists from 1972-01-01 only (`USIM_STATUS_OUT_OF_RANGE` before); for earlier dates use TDB or TT.
- Display times with `usim_time_format` (UTC, TT, TAI or TDB, Gregorian, Julian or mixed calendar). Show the scale with the time (UC-03).
- The clock (rate, pause, set time) stays inside `usim_simulation_coverage()`. When it stops at a limit, `usim_clock_get` reports `USIM_CLOCK_LIMIT_START` or `END`; show it (FR-SIM-03).
- Parse user input with `usim_time_parse` ("2026-10-21T12:00:00 UTC", "2000-01-01 TDB").

## 6. Frames and axes

- The core's native frame is the **ICRF**; displays use **ECLIPJ2000** (the ecliptic of J2000), which puts the planets near the XY plane. Body-fixed frames (`USIM_FRAME_BODY_FIXED` with a body) exist for bodies with a rotation model.
- The core is right-handed with Z up. `request.axes` maps vectors and quaternions to the host convention (ARCHITECTURE section 5.4): Unity left-handed Y up, Godot right-handed Y up, Unreal left-handed Z up. Never swap axes by hand in the host.
- Orientations are unit quaternions, scalar first (`w, x, y, z`), rotating the body's own axes into the display frame. Unity's `Quaternion` takes `(x, y, z, w)`.
- `render_radii` are the radii along the body's own axes (a toward the prime meridian, b, the polar c), already mapped to host axes: scale a unit sphere by them, then rotate.
- `usim_axes_to_host` and `usim_axes_from_host` map any other vector, such as a camera offset kept in host axes.
- States from `usim_state_at` are in km and km/s in the frame you ask for, without light-time or aberration corrections (geometric states).

## 7. Data quality and provenance

Every value the core shows has a source, and the host must show it honestly (FR-SIM-04, FR-DATA):

| Signal | Meaning | What to show |
|---|---|---|
| `USIM_SNAPSHOT_APPROXIMATE`, `USIM_STATE_APPROXIMATE`, `USIM_ORBIT_APPROXIMATE` | A body is placed at its system barycenter because the data has no planet-center ephemeris (ADR-0008) | A visible "approximate" mark; `usim_body_approximation` gives the reason |
| `USIM_SNAPSHOT_NO_POSITION` | No data covers this instant | Hide the body |
| `USIM_SNAPSHOT_NO_ORIENTATION` | No rotation model | Do not spin the body; orientation is the identity |
| `USIM_SNAPSHOT_NO_RADIUS` | No sourced radius | Draw a marker, not a sphere of made-up size |
| `USIM_SNAPSHOT_COMPRESSED` | Beyond the compression distance (section 4) | Nothing; informational |
| `USIM_SNAPSHOT_NO_LIGHT` | No light direction (the body is the star) | Draw it unlit or emissive |
| `usim_quantity.has_value == 0` | No source provides the value | "Unknown", never 0 |

`usim_body_quantity` returns each value with its unit, uncertainty, source id, locator (file and keyword) and derivation; `usim_source_at` gives the source's title, citation and license for an info panel.

## 8. Errors, memory and threads

- Fallible functions return `USIM_STATUS_*`. Treat unknown non-zero values as failures (new codes may be added). After a failure, `usim_last_error_message()` explains it; the message belongs to the calling thread and lasts until that thread's next failure.
- No exception crosses the ABI. A bug in the core reports `USIM_STATUS_INTERNAL`.
- Strings returned by the library (names, sources, reasons) live as long as the simulation. Never free them.
- Output arrays belong to the caller, with an explicit capacity.
- Functions taking `const usim_simulation*` may run on several threads at once. Functions taking a non-const pointer (the clock, `usim_orbit_path`, destroy) must not overlap any other call on the same simulation. The simple rule: drive one simulation from one thread.

## 9. Binding a new language

1. Load `universesim` (`.dylib`, `.dll` or `.so`, no `lib` prefix). Use the C calling convention.
2. Call `usim_abi_version()` and refuse to continue on a mismatch. The version increments on every breaking change.
3. Mirror the structs field for field with sequential layout and the same types (`int32_t`, `int64_t`, `uint32_t`, `double`, pointers). Compare each size with `usim_struct_size(USIM_STRUCT_*)` at startup.
4. Pass strings in as NUL-terminated UTF-8; read returned `const char*` as UTF-8 without freeing it.
5. Wrap the `usim_simulation*` in your language's owned handle type (a `SafeHandle` in C#) that calls `usim_simulation_destroy`.
6. Keep per-frame calls free of allocations on your side too: reuse the snapshot and orbit buffers.
7. Test the binding against the CLI: the same pack, time and request give the same numbers.

The Unity binding (`Runtime/Native/NativeTypes.cs`, `NativeMethods.cs`, `CoreLibrary.cs`) follows these steps; its EditMode tests check the struct sizes and fill a snapshot from the default pack.

## 10. The command line as a reference

```
usim state earth --center sun --time "2026-10-21T12:00:00 UTC"
usim snapshot --center earth --km-per-unit 1e6 --axes unity --format csv
usim orbit mars --format csv
usim catalog
```

Every host query has a CLI equivalent with the same numbers, which makes it the oracle for host tests (P5-13).
