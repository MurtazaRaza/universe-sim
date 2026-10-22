/*
 * UniverseSim C ABI.
 *
 * This header is the only interface engine hosts (Unity, Godot, Unreal, ...) use to talk to the
 * core. Conventions (see docs/ARCHITECTURE.md, section 5):
 *   - Plain C types only: fixed-width integers, double, and structs of those. No bool, no enums
 *     in structs (use int32_t), no C++ types.
 *   - Every function is safe to call from C: no exception ever crosses this boundary.
 *   - Strings are UTF-8. Strings returned by the library are owned by it; their lifetime is
 *     documented per function. Callers never free them.
 *   - Output arrays are caller-owned buffers with an explicit capacity.
 *   - Any change that breaks binary compatibility increments USIM_ABI_VERSION. Hosts compare
 *     usim_abi_version() with the value they were written against before calling anything else,
 *     and check the struct sizes they declare against usim_struct_size().
 *   - Fallible functions return a status (USIM_STATUS_*). On failure, usim_last_error_message() gives
 *     the reason, and output parameters are left unchanged unless stated otherwise.
 *   - Units: km, s, km/s, rad, km^3/s^2. Times are TDB unless stated otherwise (usim_instant).
 *
 * Thread safety: functions taking a const usim_simulation* may run on several threads at once. Functions
 * taking a non-const usim_simulation* (loading, the clock, orbit paths, destroy) must not run at the same
 * time as any other call on that simulation. Hosts call them from one thread.
 */
#ifndef USIM_USIM_H
#define USIM_USIM_H

#include <stdint.h>

#if defined(_WIN32)
#    if defined(USIM_CAPI_BUILD)
#        define USIM_API __declspec(dllexport)
#    else
#        define USIM_API __declspec(dllimport)
#    endif
#else
#    define USIM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Version of this binary interface. */
#define USIM_ABI_VERSION 3u

/* Returns the ABI version the library was built with (USIM_ABI_VERSION of its header). */
USIM_API uint32_t usim_abi_version(void);

/* Returns the core library version as "major.minor.patch".
 * The string has static storage duration and must not be freed. */
USIM_API const char* usim_version_string(void);

/* Status codes. Fallible functions return one of these as int32_t: 0 on success, otherwise the reason.
 * Each non-zero value equals the C++ usim::ErrorCode with the same name. Codes are never renumbered,
 * but new ones may be appended, so hosts must treat unknown non-zero values as generic failures. */
#define USIM_STATUS_OK 0
#define USIM_STATUS_INVALID_ARGUMENT 1     /* an argument is outside its domain */
#define USIM_STATUS_PARSE_ERROR 2          /* text could not be parsed */
#define USIM_STATUS_OUT_OF_RANGE 3         /* outside the range a model supports (e.g. UTC before 1972) */
#define USIM_STATUS_OUT_OF_COVERAGE 4      /* no data covers the requested instant */
#define USIM_STATUS_NOT_FOUND 5            /* unknown body, frame or source */
#define USIM_STATUS_UNSUPPORTED 6          /* valid, but not implemented by this version */
#define USIM_STATUS_DATA_FORMAT 7          /* malformed or inconsistent input data */
#define USIM_STATUS_INCOMPATIBLE_VERSION 8 /* input data format version not supported */
#define USIM_STATUS_IO_ERROR 9             /* a file could not be read */
#define USIM_STATUS_INTERNAL 10            /* a bug in the core */

/* Returns the lowercase name of a status code without the prefix ("ok", "invalid_argument", ...), or
 * "unknown" for an unrecognized value. The string has static storage duration and must not be freed. */
USIM_API const char* usim_status_name(int32_t status);

/* The message of the last failing call on this thread (English, no trailing period), or "" if none.
 * The string is owned by the library and valid until the next failing call on the same thread. */
USIM_API const char* usim_last_error_message(void);

/* ---------------------------------------------------------------------------------------------------
 * Plain data types. Every struct has a fixed layout; usim_struct_size() reports the library's size of
 * each, so bindings can verify their declarations at startup.
 * ------------------------------------------------------------------------------------------------- */

/* A 3-vector of doubles. */
typedef struct usim_vec3d {
    double x, y, z;
} usim_vec3d;

/* A unit quaternion, scalar first. */
typedef struct usim_quatd {
    double w, x, y, z;
} usim_quatd;

/* Identifier namespaces (usim::catalog::IdNamespace). */
#define USIM_ID_NAMESPACE_NAIF 1

/* A body identifier: namespace and value (SPEC section 7.4). id_namespace 0 means "none". */
typedef struct usim_body_id {
    int32_t id_namespace;
    int32_t reserved; /* zero */
    int64_t value;
} usim_body_id;

/* An instant on the TDB scale: whole days since J2000.0 (2000-01-01T12:00:00 TDB) plus seconds into
 * that day, in [0, 86400). seconds past J2000 = days * 86400 + seconds (SPICE ET). */
typedef struct usim_instant {
    int64_t days;
    double seconds;
} usim_instant;

/* Frame kinds (usim::frames::FrameKind). */
#define USIM_FRAME_ICRF 0
#define USIM_FRAME_ECLIPJ2000 1
#define USIM_FRAME_BODY_FIXED 2

/* A reference frame: its kind, and for USIM_FRAME_BODY_FIXED the body (the IAU body-fixed frame). */
typedef struct usim_frame {
    int32_t kind;
    int32_t reserved; /* zero */
    usim_body_id body;
} usim_frame;

/* Struct identifiers for usim_struct_size(). */
#define USIM_STRUCT_VEC3D 1
#define USIM_STRUCT_QUATD 2
#define USIM_STRUCT_BODY_ID 3
#define USIM_STRUCT_INSTANT 4
#define USIM_STRUCT_FRAME 5
#define USIM_STRUCT_BODY_INFO 6
#define USIM_STRUCT_QUANTITY 7
#define USIM_STRUCT_SOURCE 8
#define USIM_STRUCT_CLOCK_STATE 9
#define USIM_STRUCT_STATE 10
#define USIM_STRUCT_SNAPSHOT_REQUEST 11
#define USIM_STRUCT_BODY_SNAPSHOT 12
#define USIM_STRUCT_COVERAGE 13
#define USIM_STRUCT_STAR 14
#define USIM_STRUCT_RING 15

/* The size in bytes of the struct with identifier struct_id as the library declares it, or 0 for an
 * unknown identifier. */
USIM_API uint32_t usim_struct_size(int32_t struct_id);

/* ---------------------------------------------------------------------------------------------------
 * Simulation lifecycle.
 * ------------------------------------------------------------------------------------------------- */

/* A loaded data pack with its catalog, ephemeris, frames and clock. Opaque. */
typedef struct usim_simulation usim_simulation;

/* Loads the data pack at utf8_path and creates a simulation. On success *out receives a handle that the
 * caller releases with usim_simulation_destroy(). Fails with USIM_STATUS_IO_ERROR, USIM_STATUS_DATA_FORMAT
 * or USIM_STATUS_INCOMPATIBLE_VERSION. The clock starts at J2000 TDB, clamped into the coverage, at rate 1;
 * the core reads no wall clock, so hosts set the current time themselves. */
USIM_API int32_t usim_simulation_load_file(const char* utf8_path, usim_simulation** out);

/* The same from a pack held in memory (size bytes at data). The library keeps no pointer into data. */
USIM_API int32_t usim_simulation_load_memory(const void* data, uint64_t size, usim_simulation** out);

/* Releases a simulation. Strings returned for it become invalid. A null pointer is ignored. */
USIM_API void usim_simulation_destroy(usim_simulation* sim);

/* A time span (TDB), inclusive at both ends. */
typedef struct usim_coverage {
    usim_instant start;
    usim_instant end;
} usim_coverage;

/* The span in which every body with a position is covered: the clock's limits (FR-SIM-03). */
USIM_API int32_t usim_simulation_coverage(const usim_simulation* sim, usim_coverage* out);

/* ---------------------------------------------------------------------------------------------------
 * Catalog. Bodies are addressed by index, 0 to count - 1, in catalog display order. Strings returned by
 * these functions live as long as the simulation.
 * ------------------------------------------------------------------------------------------------- */

/* Body kinds (usim::catalog::BodyKind). */
#define USIM_BODY_BARYCENTER 0
#define USIM_BODY_STAR 1
#define USIM_BODY_PLANET 2
#define USIM_BODY_DWARF_PLANET 3
#define USIM_BODY_MOON 4

/* usim_body_info flags. */
#define USIM_BODY_HAS_EPHEMERIS 0x1u    /* a position is available (possibly approximate) */
#define USIM_BODY_APPROXIMATE 0x2u      /* placed at its primary (ADR-0008) */
#define USIM_BODY_HAS_ROTATION 0x4u     /* has an IAU rotation model (orientation, body-fixed frame) */
#define USIM_BODY_HAS_MEAN_RADIUS 0x8u
#define USIM_BODY_HAS_RADII 0x10u       /* triaxial radii a, b, c */
#define USIM_BODY_HAS_GM 0x20u

/* A catalog body. Values are 0 when the matching flag is not set. */
typedef struct usim_body_info {
    usim_body_id id;
    usim_body_id primary;   /* id_namespace 0 for a root */
    int32_t kind;           /* USIM_BODY_* */
    uint32_t flags;         /* USIM_BODY_* flags */
    const char* name;       /* UTF-8, for example "Earth" */
    double gm_km3_s2;
    double mean_radius_km;
    double radius_a_km;     /* equatorial, toward the prime meridian */
    double radius_b_km;     /* equatorial */
    double radius_c_km;     /* polar */
} usim_body_info;

/* The number of catalog bodies. */
USIM_API int32_t usim_body_count(const usim_simulation* sim, int32_t* out_count);

/* The body at index (0 to count - 1). */
USIM_API int32_t usim_body_info_at(const usim_simulation* sim, int32_t index, usim_body_info* out);

/* The index of the body with id. Fails with USIM_STATUS_NOT_FOUND. */
USIM_API int32_t usim_body_find(const usim_simulation* sim, usim_body_id id, int32_t* out_index);

/* Quantity fields (DATA_PACK.md). */
#define USIM_FIELD_GM 1
#define USIM_FIELD_MEAN_RADIUS 2
#define USIM_FIELD_RADIUS_A 3
#define USIM_FIELD_RADIUS_B 4
#define USIM_FIELD_RADIUS_C 5

/* Units (usim::catalog::Unit). */
#define USIM_UNIT_NONE 0
#define USIM_UNIT_KM 1
#define USIM_UNIT_S 2
#define USIM_UNIT_KM_PER_S 3
#define USIM_UNIT_RAD 4
#define USIM_UNIT_RAD_PER_S 5
#define USIM_UNIT_KM3_PER_S2 6

/* A sourced value with its provenance (FR-DATA). has_value 0 means no source provides it. */
typedef struct usim_quantity {
    double value;
    double uncertainty;      /* one sigma, when has_uncertainty */
    int32_t has_value;
    int32_t has_uncertainty;
    int32_t unit;            /* USIM_UNIT_* */
    int32_t reserved;
    const char* source_id;   /* registry id, for example "S-NAIF-PCK11" ("" when missing) */
    const char* locator;     /* file and keyword, for example "pck00011.tpc BODY399_RADII[0]" */
    const char* derivation;  /* formula and inputs for derived values, else "" */
} usim_quantity;

/* A value of the body at index. Fails with USIM_STATUS_INVALID_ARGUMENT for an unknown field. */
USIM_API int32_t usim_body_quantity(const usim_simulation* sim, int32_t index, int32_t field, usim_quantity* out);

/* Why a body is approximate (ADR-0008), or "" if it is not. */
USIM_API int32_t usim_body_approximation(const usim_simulation* sim, int32_t index, const char** out_reason);

/* A registered data source (docs/DATA_SOURCES.md). */
typedef struct usim_source {
    const char* id;
    const char* title;
    const char* provider;
    const char* url;
    const char* citation;
    const char* license;
    int32_t tier;
    int32_t reserved;
} usim_source;

/* The number of sources, and the source at index. */
USIM_API int32_t usim_source_count(const usim_simulation* sim, int32_t* out_count);
USIM_API int32_t usim_source_at(const usim_simulation* sim, int32_t index, usim_source* out);

/* ---------------------------------------------------------------------------------------------------
 * Time.
 * ------------------------------------------------------------------------------------------------- */

/* Time scales and calendars. */
#define USIM_SCALE_TDB 0
#define USIM_SCALE_TT 1
#define USIM_SCALE_TAI 2
#define USIM_SCALE_UTC 3
#define USIM_CALENDAR_GREGORIAN 0
#define USIM_CALENDAR_JULIAN 1
#define USIM_CALENDAR_MIXED 2

/* Normalizes days + seconds (any finite seconds) into an instant. */
USIM_API int32_t usim_instant_make(int64_t days, double seconds, usim_instant* out);

/* seconds past J2000 (TDB, one double; SPICE ET) as an instant. */
USIM_API int32_t usim_instant_from_seconds(double tdb_seconds, usim_instant* out);

/* Seconds from a to b (b - a), computed from the two parts. */
USIM_API double usim_instant_seconds_between(usim_instant a, usim_instant b);

/* The instant of a calendar date and time on scale (USIM_SCALE_*), Gregorian calendar. UTC is defined from
 * 1972-01-01 (USIM_STATUS_OUT_OF_RANGE before); a second of 60 is allowed at a leap second. */
USIM_API int32_t usim_time_from_fields(const usim_simulation* sim, int32_t scale, int32_t year, int32_t month,
                                       int32_t day, int32_t hour, int32_t minute, double second, usim_instant* out);

/* Parses ISO 8601 text with a scale suffix, for example "2026-10-21T12:00:00 UTC" or "J2000 TDB"
 * (usim::time::TimeScales::parse), in calendar (USIM_CALENDAR_*). */
USIM_API int32_t usim_time_parse(const usim_simulation* sim, const char* utf8_text, int32_t calendar, usim_instant* out);

/* Formats t on scale in calendar with `decimals` digits of seconds (0 to 9), for example
 * "2026-10-21T12:00:00.000 UTC", into buffer (capacity bytes, NUL-terminated). *out_length receives the
 * length without the NUL; if it is capacity or more, the text did not fit and buffer holds a truncated
 * copy. */
USIM_API int32_t usim_time_format(const usim_simulation* sim, usim_instant t, int32_t scale, int32_t calendar,
                                  int32_t decimals, char* buffer, int32_t capacity, int32_t* out_length);

/* ---------------------------------------------------------------------------------------------------
 * Clock (FR-SIM-05). Changing it requires exclusive access to the simulation.
 * ------------------------------------------------------------------------------------------------- */

/* Clock limits reached (usim::sim::ClockLimit). */
#define USIM_CLOCK_LIMIT_NONE 0
#define USIM_CLOCK_LIMIT_START 1
#define USIM_CLOCK_LIMIT_END 2

typedef struct usim_clock_state {
    usim_instant now;
    double rate;          /* simulated seconds per real second, signed */
    int32_t paused;       /* 0 or 1 */
    int32_t limit;        /* USIM_CLOCK_LIMIT_*: whether the last change stopped at a limit */
    usim_coverage limits; /* the clock never leaves these */
} usim_clock_state;

USIM_API int32_t usim_clock_get(const usim_simulation* sim, usim_clock_state* out);

/* Moves the clock by real_seconds * rate unless paused, stopping at a limit. real_seconds must be finite
 * and not negative. */
USIM_API int32_t usim_clock_advance(usim_simulation* sim, double real_seconds);
USIM_API int32_t usim_clock_set_time(usim_simulation* sim, usim_instant t);
USIM_API int32_t usim_clock_set_rate(usim_simulation* sim, double rate);
USIM_API int32_t usim_clock_set_paused(usim_simulation* sim, int32_t paused);

/* ---------------------------------------------------------------------------------------------------
 * States and orientation (FR-SIM-01, FR-SIM-02, FR-SIM-04).
 * ------------------------------------------------------------------------------------------------- */

/* State models (usim::sim::StateModel) and state flags. */
#define USIM_MODEL_EPHEMERIS 0
#define USIM_MODEL_ANALYTIC 1
#define USIM_MODEL_INTEGRATED 2
#define USIM_STATE_APPROXIMATE 0x1u /* the path to the center uses a body placed at its primary */

typedef struct usim_state {
    usim_vec3d position_km;
    usim_vec3d velocity_km_s;
    int32_t model;  /* USIM_MODEL_* */
    uint32_t flags; /* USIM_STATE_* */
} usim_state;

/* The state of body relative to center in frame at t. Fails with USIM_STATUS_OUT_OF_COVERAGE outside the
 * data (never extrapolated), USIM_STATUS_NOT_FOUND for an unknown body or frame. */
USIM_API int32_t usim_state_at(const usim_simulation* sim, usim_body_id body, usim_body_id center, usim_frame frame,
                               usim_instant t, usim_state* out);

/* The rotation from the body's IAU body-fixed frame to frame at t. Fails with USIM_STATUS_NOT_FOUND
 * without a rotation model. */
USIM_API int32_t usim_orientation_at(const usim_simulation* sim, usim_body_id body, usim_frame frame, usim_instant t,
                                     usim_quatd* out);

/* ---------------------------------------------------------------------------------------------------
 * Snapshots and orbit paths for rendering (ARCHITECTURE section 5).
 * ------------------------------------------------------------------------------------------------- */

/* Host axis conventions (usim::presentation::AxisConvention) and scale modes. */
#define USIM_AXES_CORE 0
#define USIM_AXES_UNITY 1
#define USIM_AXES_GODOT 2
#define USIM_AXES_UNREAL 3
#define USIM_SCALE_MODE_TRUE 0
#define USIM_SCALE_MODE_ENLARGED 1

/* Maps a vector from the core's axes (right-handed, Z up) to a host convention (USIM_AXES_*), and back. A
 * host uses these instead of swapping axes itself, for example to express its camera offset as origin_km. */
USIM_API int32_t usim_axes_to_host(int32_t axes, usim_vec3d core, usim_vec3d* out_host);
USIM_API int32_t usim_axes_from_host(int32_t axes, usim_vec3d host, usim_vec3d* out_core);

/* How positions become render coordinates: relative to center (usually the camera's focus), minus
 * origin_km (in frame), divided by km_per_render_unit, in the host's axes. */
typedef struct usim_snapshot_request {
    usim_body_id center;
    usim_frame frame;           /* USIM_FRAME_ICRF or USIM_FRAME_ECLIPJ2000 */
    usim_vec3d origin_km;
    double km_per_render_unit;  /* positive */
    int32_t axes;               /* USIM_AXES_* */
    int32_t scale_mode;         /* USIM_SCALE_MODE_* */
    double body_scale;          /* radius factor in USIM_SCALE_MODE_ENLARGED, at least 1 */
    double compression_distance; /* render distance beyond which distances are compressed along the line of
                                  * sight, keeping direction and apparent size (ADR-0009); 0 turns it off */
} usim_snapshot_request;

/* usim_body_snapshot flags (usim::sim::SnapshotFlag). */
#define USIM_SNAPSHOT_APPROXIMATE 0x1u    /* approximate position (ADR-0008) */
#define USIM_SNAPSHOT_NO_POSITION 0x2u    /* no data at this instant: hide the body */
#define USIM_SNAPSHOT_NO_ORIENTATION 0x4u /* no rotation model: orientation is the identity */
#define USIM_SNAPSHOT_NO_RADIUS 0x8u      /* no mean radius: render_radius is 0 */
#define USIM_SNAPSHOT_COMPRESSED 0x10u    /* beyond compression_distance: position and size scaled down */
#define USIM_SNAPSHOT_NO_LIGHT 0x20u      /* no light direction (the body is the light source, or none) */

typedef struct usim_body_snapshot {
    usim_body_id id;
    usim_vec3d render_position; /* render units, host axes, relative to the view origin */
    usim_quatd orientation;     /* body-fixed axes to the display frame, host axes */
    double render_radius;       /* mean radius in render units, scale mode applied: in USIM_SCALE_MODE_ENLARGED
                                 * planets, dwarf planets and moons grow by body_scale, capped at 40 % of a
                                 * planet-moon distance; the star keeps its true size */
    usim_vec3d render_radii;    /* radii along the body's own axes (a, b, c) as lengths along the host axes,
                                 * render units: scale the mesh by these, then rotate by orientation */
    usim_vec3d light_direction; /* unit vector toward the light source (the star), host axes, from the true
                                 * positions; zero with USIM_SNAPSHOT_NO_LIGHT */
    uint32_t flags;             /* USIM_SNAPSHOT_* */
    uint32_t reserved;
} usim_body_snapshot;

/* Fills out with up to capacity bodies, in catalog order, at the clock's time. *out_count receives the
 * number written. Allocates nothing for a valid request. */
USIM_API int32_t usim_fill_snapshot(const usim_simulation* sim, const usim_snapshot_request* request,
                                    usim_body_snapshot* out, int32_t capacity, int32_t* out_count);

/* usim_orbit_path flags. */
#define USIM_ORBIT_FULL_REVOLUTION 0x1u /* the path spans one full turn */
#define USIM_ORBIT_APPROXIMATE 0x2u     /* approximate positions (ADR-0008) */

/* The body that the orbit line of body is usually drawn around (the Sun, or a moon's planet). */
USIM_API int32_t usim_default_orbit_center(const usim_simulation* sim, usim_body_id body, usim_body_id* out_center);

/* One revolution of body around orbit_center at the clock's time, as render positions for the view in
 * request (whose center may differ from orbit_center), compressed like snapshot positions. Paths are cached in the simulation and sampled
 * again only after a large time jump, so per-frame calls are cheap. Writes up to capacity points;
 * *out_count receives the path's full point count (larger than capacity if it did not fit) and *out_flags
 * the USIM_ORBIT_* flags. Requires exclusive access to the simulation (it updates the cache). */
USIM_API int32_t usim_orbit_path(usim_simulation* sim, usim_body_id body, usim_body_id orbit_center,
                                 const usim_snapshot_request* request, usim_vec3d* out_positions, int32_t capacity,
                                 int32_t* out_count, uint32_t* out_flags);

/* ---------------------------------------------------------------------------------------------------
 * Sky background and rings (pack format 1.2; SPEC FR-REN-09, FR-REN-10). ABI version 3.
 * ------------------------------------------------------------------------------------------------- */

/* Id namespace of Bright Star Catalogue (Harvard Revised) numbers. */
#define USIM_ID_NAMESPACE_HR 4

/* usim_star flags. */
#define USIM_STAR_HAS_COLOR 0x1u /* b_minus_v is the catalog's B-V color index */

/* One star of the sky background. Stars are infinitely far away: one direction for every viewpoint. */
typedef struct usim_star {
    usim_vec3d direction; /* unit vector toward the star at the clock's time, display frame, host axes */
    double v_magnitude;   /* V magnitude */
    double b_minus_v;     /* B-V color index, magnitudes; 0 without USIM_STAR_HAS_COLOR */
    uint32_t flags;       /* USIM_STAR_* */
    uint32_t reserved;
} usim_star;

/* The number of stars (0 when the pack has no star catalog). */
USIM_API int32_t usim_star_count(const usim_simulation* sim, int32_t* out_count);

/* Where the star catalog comes from: the index of its source (usim_source_at), the file and columns, and
 * how they became the values. Fails with USIM_STATUS_NOT_FOUND when the pack has no star catalog. */
USIM_API int32_t usim_star_catalog_source(const usim_simulation* sim, int32_t* out_source_index,
                                          const char** out_locator, const char** out_derivation);

/* Fills out with up to capacity stars, in catalog order, at the clock's time, in request's frame and axes
 * (the other request fields do not apply to directions). *out_count receives the number written. Allocates
 * nothing. */
USIM_API int32_t usim_fill_stars(const usim_simulation* sim, const usim_snapshot_request* request, usim_star* out,
                                 int32_t capacity, int32_t* out_count);

/* usim_ring flags. */
#define USIM_RING_GAP 0x1u               /* an empty region within a ring */
#define USIM_RING_HAS_OPTICAL_DEPTH 0x2u /* the optical depth fields are set */

/* A ring region or gap in the equatorial plane of a body (the XY plane of its IAU frame): visual-only
 * geometry. Strings live as long as the simulation. */
typedef struct usim_ring {
    int32_t body_index;              /* the planet, index into the catalog */
    uint32_t flags;                  /* USIM_RING_* */
    double inner_radius_km;
    double outer_radius_km;
    double optical_depth_smallest;   /* normal optical depth range, with USIM_RING_HAS_OPTICAL_DEPTH */
    double optical_depth_largest;
    int32_t radii_source_index;      /* usim_source_at */
    int32_t optical_depth_source_index; /* -1 without an optical depth */
    const char* name;                /* for example "C Ring" */
    const char* radii_locator;       /* table and row, with the derivation if the radii were computed */
    const char* optical_depth_locator; /* "" without an optical depth */
} usim_ring;

/* The number of ring regions and gaps, sorted by body and inner radius, and the one at index. */
USIM_API int32_t usim_ring_count(const usim_simulation* sim, int32_t* out_count);
USIM_API int32_t usim_ring_at(const usim_simulation* sim, int32_t index, usim_ring* out);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* USIM_USIM_H */
