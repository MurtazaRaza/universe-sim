# ADR-0003: Units, time scale and reference frames

| | |
|---|---|
| Status | Accepted |
| Date | 2026-10-20 |

## Context

Mixing units, time scales or frames is the most common source of silent errors in astronomy software. The primary data (JPL DE ephemerides, NAIF kernels) has native conventions: km and seconds, TDB, and the ICRF. The renderer (Unity) has different ones: single precision, left-handed and Y up.

## Decision

1. **Units:** km, s, km/s, rad and GM in km^3/s^2 throughout the core. Mass is never a primary value. It may be derived as GM / G and flagged as derived.
2. **Time:** the internal time scale is TDB, stored as a two-part `Instant` relative to J2000.0 (JD 2451545.0 TDB). UTC, TAI and TT exist only at the edges (user input and display), and conversions use the leap second table from the data pack.
3. **Frames:** the ICRF is the native inertial frame (it is what DE kernels use; SPICE calls it `J2000`). ECLIPJ2000 is the default frame for rendering and display. Body-fixed frames come from IAU rotation models. The default center is the Solar System Barycenter (NAIF 0).
4. **Handedness:** the core is right-handed. Conversion to a host convention happens only in `usim::presentation::AxisConvention`, applied by the core when it fills a snapshot. For Unity the mapping is (X, Y, Z) to (X, Z, Y), which puts ecliptic north at Unity up.
5. **Precision:** double precision everywhere in the core. Positions are re-centered on a double-precision view origin before any host converts them to float.
6. **Naming:** public members that carry a unit say so in their name or doc comment (`position_km`, `velocity_km_s`), and frame and time scale parameters are explicit, not implied.

## Consequences

- Data from JPL and NAIF needs no unit conversion on input.
- Double precision km resolves about 1 mm at Neptune's distance (4.5e9 km) and about 2 m at one light-year. Interstellar scales will need hierarchical local frames (planned in Phase 9).
- Hosts must use the provided axis mapping. Mapping axes by hand in a host is a defect.
- Historical dates before 1972 cannot be shown in UTC. A ΔT model is a separate decision (OQ-04).

## Alternatives considered

- **SI meters.** No precision benefit in double, and every kernel value would need conversion. Rejected.
- **au and days** (common in celestial mechanics). Convenient for integrators, but the conversions would sit at every boundary with the data. Integrators may use scaled units internally if they convert at their own boundary.
- **Seconds since J2000 in a single double.** Simpler, but resolution degrades over long spans: about 4 µs at the edges of DE440's span and about 60 µs at the edges of DE441's. The two-part form costs little and keeps the clock exact under repeated small increments.
- **Ecliptic as the native frame.** It would require rotating all kernel data on load and would differ from the SPICE references used for validation.
