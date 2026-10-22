// Implementation of the C ABI declared in usim/usim.h.
//
// Rules for every function in this file: no exception may escape (each body runs inside guarded(), which
// converts exceptions to USIM_STATUS_INTERNAL), validate every pointer argument, and pass only C types.

#include "usim/result.hpp"
#include "usim/sim/orbit_path.hpp"
#include "usim/sim/simulation.hpp"
#include "usim/usim.h"
#include "usim/version.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <exception>
#include <filesystem>
#include <limits>
#include <new>
#include <optional>
#include <span>
#include <string>
#include <utility>

// The status codes are the ErrorCode values (P1-08). Keep these in step with both enumerations.
static_assert(USIM_STATUS_INVALID_ARGUMENT == static_cast<std::int32_t>(usim::ErrorCode::invalid_argument));
static_assert(USIM_STATUS_PARSE_ERROR == static_cast<std::int32_t>(usim::ErrorCode::parse_error));
static_assert(USIM_STATUS_OUT_OF_RANGE == static_cast<std::int32_t>(usim::ErrorCode::out_of_range));
static_assert(USIM_STATUS_OUT_OF_COVERAGE == static_cast<std::int32_t>(usim::ErrorCode::out_of_coverage));
static_assert(USIM_STATUS_NOT_FOUND == static_cast<std::int32_t>(usim::ErrorCode::not_found));
static_assert(USIM_STATUS_UNSUPPORTED == static_cast<std::int32_t>(usim::ErrorCode::unsupported));
static_assert(USIM_STATUS_DATA_FORMAT == static_cast<std::int32_t>(usim::ErrorCode::data_format));
static_assert(USIM_STATUS_INCOMPATIBLE_VERSION ==
              static_cast<std::int32_t>(usim::ErrorCode::incompatible_version));
static_assert(USIM_STATUS_IO_ERROR == static_cast<std::int32_t>(usim::ErrorCode::io_error));
static_assert(USIM_STATUS_INTERNAL == static_cast<std::int32_t>(usim::ErrorCode::internal));
static_assert(USIM_STATUS_INTERNAL == usim::k_max_error_code, "a new ErrorCode needs a USIM_STATUS_ code");

// The other codes the header repeats (enums of the core).
static_assert(USIM_ID_NAMESPACE_NAIF == static_cast<std::int32_t>(usim::catalog::IdNamespace::naif));
static_assert(USIM_FRAME_ICRF == static_cast<std::int32_t>(usim::frames::FrameKind::icrf));
static_assert(USIM_FRAME_ECLIPJ2000 == static_cast<std::int32_t>(usim::frames::FrameKind::eclipj2000));
static_assert(USIM_FRAME_BODY_FIXED == static_cast<std::int32_t>(usim::frames::FrameKind::body_fixed));
static_assert(USIM_BODY_MOON == static_cast<std::int32_t>(usim::catalog::BodyKind::moon));
static_assert(USIM_BODY_DWARF_PLANET == static_cast<std::int32_t>(usim::catalog::BodyKind::dwarf_planet));
static_assert(USIM_UNIT_KM == static_cast<std::int32_t>(usim::catalog::Unit::km));
static_assert(USIM_UNIT_KM3_PER_S2 == static_cast<std::int32_t>(usim::catalog::Unit::km3_per_s2));
static_assert(USIM_SCALE_TDB == static_cast<std::int32_t>(usim::time::TimeScale::tdb));
static_assert(USIM_SCALE_UTC == static_cast<std::int32_t>(usim::time::TimeScale::utc));
static_assert(USIM_CALENDAR_GREGORIAN == static_cast<std::int32_t>(usim::time::Calendar::gregorian));
static_assert(USIM_CALENDAR_MIXED == static_cast<std::int32_t>(usim::time::Calendar::mixed));
static_assert(USIM_CLOCK_LIMIT_END == static_cast<std::int32_t>(usim::sim::ClockLimit::end));
static_assert(USIM_MODEL_INTEGRATED == static_cast<std::int32_t>(usim::sim::StateModel::integrated));
static_assert(USIM_AXES_UNITY == static_cast<std::int32_t>(usim::presentation::AxisConvention::unity));
static_assert(USIM_AXES_UNREAL == static_cast<std::int32_t>(usim::presentation::AxisConvention::unreal));
static_assert(USIM_SCALE_MODE_ENLARGED ==
              static_cast<std::int32_t>(usim::presentation::ScaleMode::enlarged_bodies));
static_assert(USIM_SNAPSHOT_APPROXIMATE == usim::sim::snapshot_approximate);
static_assert(USIM_SNAPSHOT_NO_POSITION == usim::sim::snapshot_no_position);
static_assert(USIM_SNAPSHOT_NO_ORIENTATION == usim::sim::snapshot_no_orientation);
static_assert(USIM_SNAPSHOT_NO_RADIUS == usim::sim::snapshot_no_radius);
static_assert(USIM_SNAPSHOT_COMPRESSED == usim::sim::snapshot_compressed);
static_assert(USIM_SNAPSHOT_NO_LIGHT == usim::sim::snapshot_no_light);
static_assert(USIM_ID_NAMESPACE_HR == static_cast<std::int32_t>(usim::catalog::IdNamespace::hr));
static_assert(USIM_STAR_HAS_COLOR == usim::sim::star_has_color);

struct usim_simulation {
    usim_simulation(usim::sim::Simulation s)
        : sim(std::move(s)), scales(sim.pack().time_scales()), orbits(sim.ephemeris(), sim.coverage()) {}

    usim::sim::Simulation sim;
    usim::time::TimeScales scales;
    usim::sim::OrbitPathCache orbits; // points into sim, which lives in this object
};

namespace {

thread_local std::string t_last_error;

// Records `message` as the last error of this thread and returns `code` as a status.
std::int32_t fail(usim::ErrorCode code, const char* message) noexcept {
    try {
        t_last_error = message;
    } catch (...) {
        t_last_error.clear(); // keep the code even if the message cannot be stored
    }
    return static_cast<std::int32_t>(code);
}

std::int32_t fail(const usim::Error& error) noexcept {
    return fail(error.code, error.message.c_str());
}

std::int32_t null_argument(const char* name) noexcept {
    try {
        t_last_error = std::string("argument ") + name + " is null";
    } catch (...) {
        t_last_error.clear();
    }
    return USIM_STATUS_INVALID_ARGUMENT;
}

// Runs `body` and converts any exception into USIM_STATUS_INTERNAL.
template <typename Body>
std::int32_t guarded(Body&& body) noexcept {
    try {
        return body();
    } catch (const std::bad_alloc&) {
        return fail(usim::ErrorCode::internal, "out of memory");
    } catch (const std::exception& e) {
        return fail(usim::ErrorCode::internal, e.what());
    } catch (...) {
        return fail(usim::ErrorCode::internal, "unexpected exception");
    }
}

usim_body_id to_c(usim::catalog::BodyId id) noexcept {
    return {static_cast<std::int32_t>(id.id_namespace), 0, id.value};
}

usim::catalog::BodyId from_c(usim_body_id id) noexcept {
    return {static_cast<usim::catalog::IdNamespace>(id.id_namespace), id.value};
}

usim_vec3d to_c(const usim::math::Vec3& v) noexcept {
    return {v.x, v.y, v.z};
}

usim_quatd to_c(const usim::math::Quat& q) noexcept {
    return {q.w, q.x, q.y, q.z};
}

usim_instant to_c(usim::time::Instant t) noexcept {
    return {t.days_since_j2000(), t.seconds_of_day()};
}

// The instant of a C instant, or nothing if it is not normalized and finite.
std::optional<usim::time::Instant> from_c(usim_instant t) noexcept {
    if (!std::isfinite(t.seconds) || t.seconds < 0.0 || t.seconds >= 86400.0) {
        return std::nullopt;
    }
    const usim::time::Instant instant = usim::time::Instant::from_days_and_seconds(t.days, t.seconds);
    return instant.is_valid() ? std::optional(instant) : std::nullopt;
}

std::optional<usim::frames::FrameId> from_c(usim_frame frame) noexcept {
    switch (frame.kind) {
    case USIM_FRAME_ICRF:
        return usim::frames::FrameId::icrf();
    case USIM_FRAME_ECLIPJ2000:
        return usim::frames::FrameId::eclipj2000();
    case USIM_FRAME_BODY_FIXED:
        return usim::frames::FrameId::body_fixed(from_c(frame.body));
    default:
        return std::nullopt;
    }
}

std::optional<usim::sim::SnapshotRequest> from_c(const usim_snapshot_request& r) noexcept {
    const std::optional<usim::frames::FrameId> frame = from_c(r.frame);
    if (!frame) {
        return std::nullopt;
    }
    usim::sim::SnapshotRequest request;
    request.center = from_c(r.center);
    request.frame = *frame;
    request.view.origin_km = {r.origin_km.x, r.origin_km.y, r.origin_km.z};
    request.view.km_per_render_unit = r.km_per_render_unit;
    request.view.axes = static_cast<usim::presentation::AxisConvention>(r.axes);
    request.view.scale_mode = static_cast<usim::presentation::ScaleMode>(r.scale_mode);
    request.view.body_scale = r.body_scale;
    request.view.compression_distance = r.compression_distance;
    return request;
}

std::int32_t invalid(const char* message) noexcept {
    return fail(usim::ErrorCode::invalid_argument, message);
}

// The catalog body at `index`, or nullptr after recording an error.
const usim::catalog::Body* body_at(const usim_simulation* sim, std::int32_t index,
                                   std::int32_t& status) noexcept {
    const auto bodies = sim->sim.catalog().bodies();
    if (index < 0 || static_cast<std::size_t>(index) >= bodies.size()) {
        status = invalid("body index out of range");
        return nullptr;
    }
    return &bodies[static_cast<std::size_t>(index)];
}

std::int32_t load(usim::Result<usim::sim::Simulation> loaded, usim_simulation** out) {
    if (!loaded) {
        return fail(loaded.error());
    }
    *out = new usim_simulation(std::move(loaded).value());
    return USIM_STATUS_OK;
}

} // namespace

extern "C" {

USIM_API uint32_t usim_abi_version(void) {
    return USIM_ABI_VERSION;
}

USIM_API const char* usim_version_string(void) {
    // version_string() views a null-terminated literal with static storage duration.
    return usim::version_string().data();
}

USIM_API const char* usim_status_name(int32_t status) {
    if (status == USIM_STATUS_OK) {
        return "ok";
    }
    if (status < 1 || status > usim::k_max_error_code) {
        return "unknown";
    }
    // to_string() views string literals, which are null-terminated with static storage duration.
    return usim::to_string(static_cast<usim::ErrorCode>(status)).data();
}

USIM_API const char* usim_last_error_message(void) {
    return t_last_error.c_str();
}

USIM_API uint32_t usim_struct_size(int32_t struct_id) {
    switch (struct_id) {
    case USIM_STRUCT_VEC3D:
        return sizeof(usim_vec3d);
    case USIM_STRUCT_QUATD:
        return sizeof(usim_quatd);
    case USIM_STRUCT_BODY_ID:
        return sizeof(usim_body_id);
    case USIM_STRUCT_INSTANT:
        return sizeof(usim_instant);
    case USIM_STRUCT_FRAME:
        return sizeof(usim_frame);
    case USIM_STRUCT_BODY_INFO:
        return sizeof(usim_body_info);
    case USIM_STRUCT_QUANTITY:
        return sizeof(usim_quantity);
    case USIM_STRUCT_SOURCE:
        return sizeof(usim_source);
    case USIM_STRUCT_CLOCK_STATE:
        return sizeof(usim_clock_state);
    case USIM_STRUCT_STATE:
        return sizeof(usim_state);
    case USIM_STRUCT_SNAPSHOT_REQUEST:
        return sizeof(usim_snapshot_request);
    case USIM_STRUCT_BODY_SNAPSHOT:
        return sizeof(usim_body_snapshot);
    case USIM_STRUCT_COVERAGE:
        return sizeof(usim_coverage);
    case USIM_STRUCT_STAR:
        return sizeof(usim_star);
    case USIM_STRUCT_RING:
        return sizeof(usim_ring);
    default:
        return 0;
    }
}

USIM_API int32_t usim_simulation_load_file(const char* utf8_path, usim_simulation** out) {
    return guarded([&] {
        if (utf8_path == nullptr || out == nullptr) {
            return null_argument(utf8_path == nullptr ? "utf8_path" : "out");
        }
        // The path is UTF-8 on every platform; u8path decodes it as such (std::filesystem::path(const
        // char8_t*) in C++20 terms).
        const std::u8string text(reinterpret_cast<const char8_t*>(utf8_path));
        return load(usim::sim::Simulation::load_file(std::filesystem::path(text)), out);
    });
}

USIM_API int32_t usim_simulation_load_memory(const void* data, uint64_t size, usim_simulation** out) {
    return guarded([&] {
        if ((data == nullptr && size > 0) || out == nullptr) {
            return null_argument(out == nullptr ? "out" : "data");
        }
        if (size > std::numeric_limits<std::size_t>::max()) {
            return invalid("the pack is larger than this platform can address");
        }
        const std::span<const std::byte> bytes(static_cast<const std::byte*>(data),
                                               static_cast<std::size_t>(size));
        return load(usim::sim::Simulation::load(bytes), out);
    });
}

USIM_API void usim_simulation_destroy(usim_simulation* sim) {
    delete sim; // destructors do not throw
}

USIM_API int32_t usim_simulation_coverage(const usim_simulation* sim, usim_coverage* out) {
    if (sim == nullptr || out == nullptr) {
        return null_argument(sim == nullptr ? "sim" : "out");
    }
    *out = {to_c(sim->sim.coverage().start), to_c(sim->sim.coverage().end)};
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_body_count(const usim_simulation* sim, int32_t* out_count) {
    if (sim == nullptr || out_count == nullptr) {
        return null_argument(sim == nullptr ? "sim" : "out_count");
    }
    *out_count = static_cast<std::int32_t>(sim->sim.catalog().size());
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_body_info_at(const usim_simulation* sim, int32_t index, usim_body_info* out) {
    return guarded([&] {
        if (sim == nullptr || out == nullptr) {
            return null_argument(sim == nullptr ? "sim" : "out");
        }
        std::int32_t status = USIM_STATUS_OK;
        const usim::catalog::Body* body = body_at(sim, index, status);
        if (body == nullptr) {
            return status;
        }
        const auto& chain = sim->sim.ephemeris();
        usim_body_info info{};
        info.id = to_c(body->id);
        info.primary = body->primary ? to_c(*body->primary) : usim_body_id{0, 0, 0};
        info.kind = static_cast<std::int32_t>(body->kind);
        info.name = body->name.c_str();
        if (chain.contains(body->id)) {
            info.flags |= USIM_BODY_HAS_EPHEMERIS;
        }
        if (chain.approximation(body->id) != nullptr) {
            info.flags |= USIM_BODY_APPROXIMATE;
        }
        if (sim->sim.frames().find_rotation(body->id) != nullptr) {
            info.flags |= USIM_BODY_HAS_ROTATION;
        }
        if (body->mean_radius.has_value()) {
            info.flags |= USIM_BODY_HAS_MEAN_RADIUS;
            info.mean_radius_km = *body->mean_radius.value;
        }
        if (body->radius_a.has_value() && body->radius_b.has_value() && body->radius_c.has_value()) {
            info.flags |= USIM_BODY_HAS_RADII;
            info.radius_a_km = *body->radius_a.value;
            info.radius_b_km = *body->radius_b.value;
            info.radius_c_km = *body->radius_c.value;
        }
        if (body->gm.has_value()) {
            info.flags |= USIM_BODY_HAS_GM;
            info.gm_km3_s2 = *body->gm.value;
        }
        *out = info;
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_body_find(const usim_simulation* sim, usim_body_id id, int32_t* out_index) {
    return guarded([&] {
        if (sim == nullptr || out_index == nullptr) {
            return null_argument(sim == nullptr ? "sim" : "out_index");
        }
        const auto bodies = sim->sim.catalog().bodies();
        const usim::catalog::BodyId wanted = from_c(id);
        const auto it =
            std::find_if(bodies.begin(), bodies.end(), [&](const auto& b) { return b.id == wanted; });
        if (it == bodies.end()) {
            return fail(usim::ErrorCode::not_found,
                        ("no body " + usim::catalog::to_string(wanted) + " in the catalog").c_str());
        }
        *out_index = static_cast<std::int32_t>(it - bodies.begin());
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_body_quantity(const usim_simulation* sim, int32_t index, int32_t field,
                                    usim_quantity* out) {
    return guarded([&] {
        if (sim == nullptr || out == nullptr) {
            return null_argument(sim == nullptr ? "sim" : "out");
        }
        std::int32_t status = USIM_STATUS_OK;
        const usim::catalog::Body* body = body_at(sim, index, status);
        if (body == nullptr) {
            return status;
        }
        const usim::catalog::Quantity* q = nullptr;
        switch (field) {
        case USIM_FIELD_GM:
            q = &body->gm;
            break;
        case USIM_FIELD_MEAN_RADIUS:
            q = &body->mean_radius;
            break;
        case USIM_FIELD_RADIUS_A:
            q = &body->radius_a;
            break;
        case USIM_FIELD_RADIUS_B:
            q = &body->radius_b;
            break;
        case USIM_FIELD_RADIUS_C:
            q = &body->radius_c;
            break;
        default:
            return invalid("unknown quantity field");
        }
        usim_quantity result{};
        result.has_value = q->has_value() ? 1 : 0;
        result.value = q->value.value_or(0.0);
        result.has_uncertainty = q->uncertainty ? 1 : 0;
        result.uncertainty = q->uncertainty.value_or(0.0);
        result.unit = static_cast<std::int32_t>(q->unit);
        result.source_id = q->source.source_id.c_str();
        result.locator = q->source.locator.c_str();
        result.derivation = q->derivation.c_str();
        *out = result;
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_body_approximation(const usim_simulation* sim, int32_t index, const char** out_reason) {
    return guarded([&] {
        if (sim == nullptr || out_reason == nullptr) {
            return null_argument(sim == nullptr ? "sim" : "out_reason");
        }
        std::int32_t status = USIM_STATUS_OK;
        const usim::catalog::Body* body = body_at(sim, index, status);
        if (body == nullptr) {
            return status;
        }
        const usim::data::PositionApproximation* a = sim->sim.ephemeris().approximation(body->id);
        *out_reason = a != nullptr ? a->reason.c_str() : "";
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_source_count(const usim_simulation* sim, int32_t* out_count) {
    if (sim == nullptr || out_count == nullptr) {
        return null_argument(sim == nullptr ? "sim" : "out_count");
    }
    *out_count = static_cast<std::int32_t>(sim->sim.catalog().sources().size());
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_source_at(const usim_simulation* sim, int32_t index, usim_source* out) {
    if (sim == nullptr || out == nullptr) {
        return null_argument(sim == nullptr ? "sim" : "out");
    }
    const auto sources = sim->sim.catalog().sources();
    if (index < 0 || static_cast<std::size_t>(index) >= sources.size()) {
        return invalid("source index out of range");
    }
    const usim::catalog::Source& s = sources[static_cast<std::size_t>(index)];
    *out = {s.id.c_str(),
            s.title.c_str(),
            s.provider.c_str(),
            s.url.c_str(),
            s.citation.c_str(),
            s.license.c_str(),
            s.tier,
            0};
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_instant_make(int64_t days, double seconds, usim_instant* out) {
    if (out == nullptr) {
        return null_argument("out");
    }
    const usim::time::Instant t = usim::time::Instant::from_days_and_seconds(days, seconds);
    if (!t.is_valid()) {
        return invalid("the instant is not finite or too far from J2000");
    }
    *out = to_c(t);
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_instant_from_seconds(double tdb_seconds, usim_instant* out) {
    return usim_instant_make(0, tdb_seconds, out);
}

USIM_API double usim_instant_seconds_between(usim_instant a, usim_instant b) {
    return static_cast<double>(b.days - a.days) * 86400.0 + (b.seconds - a.seconds);
}

USIM_API int32_t usim_time_from_fields(const usim_simulation* sim, int32_t scale, int32_t year, int32_t month,
                                       int32_t day, int32_t hour, int32_t minute, double second,
                                       usim_instant* out) {
    return guarded([&] {
        if (sim == nullptr || out == nullptr) {
            return null_argument(sim == nullptr ? "sim" : "out");
        }
        if (scale < USIM_SCALE_TDB || scale > USIM_SCALE_UTC) {
            return invalid("unknown time scale");
        }
        const usim::time::DateTime fields{{year, month, day}, hour, minute, second};
        const auto t = sim->scales.to_instant(fields, static_cast<usim::time::TimeScale>(scale),
                                              usim::time::Calendar::gregorian);
        if (!t) {
            return fail(t.error());
        }
        *out = to_c(t.value());
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_time_parse(const usim_simulation* sim, const char* utf8_text, int32_t calendar,
                                 usim_instant* out) {
    return guarded([&] {
        if (sim == nullptr || utf8_text == nullptr || out == nullptr) {
            return null_argument(sim == nullptr ? "sim" : (utf8_text == nullptr ? "utf8_text" : "out"));
        }
        if (calendar < USIM_CALENDAR_GREGORIAN || calendar > USIM_CALENDAR_MIXED) {
            return invalid("unknown calendar");
        }
        const auto t = sim->scales.parse(utf8_text, static_cast<usim::time::Calendar>(calendar));
        if (!t) {
            return fail(t.error());
        }
        *out = to_c(t.value());
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_time_format(const usim_simulation* sim, usim_instant t, int32_t scale, int32_t calendar,
                                  int32_t decimals, char* buffer, int32_t capacity, int32_t* out_length) {
    return guarded([&] {
        if (sim == nullptr || out_length == nullptr || (buffer == nullptr && capacity > 0)) {
            return null_argument(sim == nullptr ? "sim" : (out_length == nullptr ? "out_length" : "buffer"));
        }
        if (scale < USIM_SCALE_TDB || scale > USIM_SCALE_UTC || calendar < USIM_CALENDAR_GREGORIAN ||
            calendar > USIM_CALENDAR_MIXED || decimals < 0 || decimals > 9 || capacity < 0) {
            return invalid("unknown scale or calendar, decimals outside 0 to 9, or negative capacity");
        }
        const auto instant = from_c(t);
        if (!instant) {
            return invalid("the instant is not normalized");
        }
        const auto text = sim->scales.format(*instant, static_cast<usim::time::TimeScale>(scale),
                                             static_cast<usim::time::Calendar>(calendar), decimals);
        if (!text) {
            return fail(text.error());
        }
        if (capacity > 0) {
            const std::size_t n = std::min(text.value().size(), static_cast<std::size_t>(capacity) - 1);
            std::memcpy(buffer, text.value().data(), n);
            buffer[n] = '\0';
        }
        *out_length = static_cast<std::int32_t>(text.value().size());
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_clock_get(const usim_simulation* sim, usim_clock_state* out) {
    if (sim == nullptr || out == nullptr) {
        return null_argument(sim == nullptr ? "sim" : "out");
    }
    const usim::sim::SimulationClock& clock = sim->sim.clock();
    *out = {to_c(clock.now()),
            clock.rate(),
            clock.paused() ? 1 : 0,
            static_cast<std::int32_t>(clock.limit()),
            {to_c(clock.limits().start), to_c(clock.limits().end)}};
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_clock_advance(usim_simulation* sim, double real_seconds) {
    return guarded([&] {
        if (sim == nullptr) {
            return null_argument("sim");
        }
        const auto r = sim->sim.clock().advance(real_seconds);
        return r ? USIM_STATUS_OK : fail(r.error());
    });
}

USIM_API int32_t usim_clock_set_time(usim_simulation* sim, usim_instant t) {
    return guarded([&] {
        if (sim == nullptr) {
            return null_argument("sim");
        }
        const auto instant = from_c(t);
        if (!instant) {
            return invalid("the instant is not normalized");
        }
        const auto r = sim->sim.clock().set_time(*instant);
        return r ? USIM_STATUS_OK : fail(r.error());
    });
}

USIM_API int32_t usim_clock_set_rate(usim_simulation* sim, double rate) {
    return guarded([&] {
        if (sim == nullptr) {
            return null_argument("sim");
        }
        const auto r = sim->sim.clock().set_rate(rate);
        return r ? USIM_STATUS_OK : fail(r.error());
    });
}

USIM_API int32_t usim_clock_set_paused(usim_simulation* sim, int32_t paused) {
    if (sim == nullptr) {
        return null_argument("sim");
    }
    sim->sim.clock().set_paused(paused != 0);
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_state_at(const usim_simulation* sim, usim_body_id body, usim_body_id center,
                               usim_frame frame, usim_instant t, usim_state* out) {
    return guarded([&] {
        if (sim == nullptr || out == nullptr) {
            return null_argument(sim == nullptr ? "sim" : "out");
        }
        const auto frame_id = from_c(frame);
        const auto instant = from_c(t);
        if (!frame_id || !instant) {
            return invalid(!frame_id ? "unknown frame kind" : "the instant is not normalized");
        }
        const auto s = sim->sim.state(from_c(body), from_c(center), *frame_id, *instant);
        if (!s) {
            return fail(s.error());
        }
        *out = {to_c(s.value().state.position_km), to_c(s.value().state.velocity_km_s),
                static_cast<std::int32_t>(s.value().quality.model),
                s.value().quality.approximate ? USIM_STATE_APPROXIMATE : 0u};
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_orientation_at(const usim_simulation* sim, usim_body_id body, usim_frame frame,
                                     usim_instant t, usim_quatd* out) {
    return guarded([&] {
        if (sim == nullptr || out == nullptr) {
            return null_argument(sim == nullptr ? "sim" : "out");
        }
        const auto frame_id = from_c(frame);
        const auto instant = from_c(t);
        if (!frame_id || !instant) {
            return invalid(!frame_id ? "unknown frame kind" : "the instant is not normalized");
        }
        const auto q = sim->sim.orientation(from_c(body), *frame_id, *instant);
        if (!q) {
            return fail(q.error());
        }
        *out = to_c(q.value());
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_axes_to_host(int32_t axes, usim_vec3d core, usim_vec3d* out_host) {
    if (out_host == nullptr) {
        return null_argument("out_host");
    }
    if (axes < USIM_AXES_CORE || axes > USIM_AXES_UNREAL) {
        return invalid("unknown axis convention");
    }
    *out_host = to_c(usim::presentation::to_host(static_cast<usim::presentation::AxisConvention>(axes),
                                                 usim::math::Vec3{core.x, core.y, core.z}));
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_axes_from_host(int32_t axes, usim_vec3d host, usim_vec3d* out_core) {
    if (out_core == nullptr) {
        return null_argument("out_core");
    }
    if (axes < USIM_AXES_CORE || axes > USIM_AXES_UNREAL) {
        return invalid("unknown axis convention");
    }
    *out_core = to_c(usim::presentation::from_host(static_cast<usim::presentation::AxisConvention>(axes),
                                                   usim::math::Vec3{host.x, host.y, host.z}));
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_fill_snapshot(const usim_simulation* sim, const usim_snapshot_request* request,
                                    usim_body_snapshot* out, int32_t capacity, int32_t* out_count) {
    return guarded([&] {
        if (sim == nullptr || request == nullptr || out_count == nullptr ||
            (out == nullptr && capacity > 0)) {
            return null_argument(
                sim == nullptr
                    ? "sim"
                    : (request == nullptr ? "request" : (out_count == nullptr ? "out_count" : "out")));
        }
        if (capacity < 0) {
            return invalid("negative capacity");
        }
        const auto r = from_c(*request);
        if (!r) {
            return invalid("unknown frame kind");
        }
        // Fill in chunks through a buffer on the stack, so no memory is allocated and no state is shared
        // between threads.
        std::array<usim::sim::BodySnapshot, 16> chunk;
        const std::size_t wanted = std::min(static_cast<std::size_t>(capacity), sim->sim.snapshot_size());
        std::size_t done = 0;
        do {
            const std::size_t n = std::min(chunk.size(), wanted - done);
            const auto filled = sim->sim.fill_snapshot(*r, std::span(chunk.data(), n), done);
            if (!filled) {
                return fail(filled.error());
            }
            for (std::size_t i = 0; i < filled.value(); ++i) {
                const usim::sim::BodySnapshot& s = chunk[i];
                out[done + i] = {to_c(s.body),
                                 to_c(s.render_position),
                                 to_c(s.orientation),
                                 s.render_radius,
                                 to_c(s.render_radii),
                                 to_c(s.light_direction),
                                 s.flags,
                                 0};
            }
            done += filled.value();
            if (filled.value() == 0) {
                break;
            }
        } while (done < wanted);
        *out_count = static_cast<std::int32_t>(done);
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_default_orbit_center(const usim_simulation* sim, usim_body_id body,
                                           usim_body_id* out_center) {
    return guarded([&] {
        if (sim == nullptr || out_center == nullptr) {
            return null_argument(sim == nullptr ? "sim" : "out_center");
        }
        const auto center = sim->sim.default_orbit_center(from_c(body));
        if (!center) {
            return fail(center.error());
        }
        *out_center = to_c(center.value());
        return USIM_STATUS_OK;
    });
}

USIM_API int32_t usim_orbit_path(usim_simulation* sim, usim_body_id body, usim_body_id orbit_center,
                                 const usim_snapshot_request* request, usim_vec3d* out_positions,
                                 int32_t capacity, int32_t* out_count, uint32_t* out_flags) {
    return guarded([&] {
        if (sim == nullptr || request == nullptr || out_count == nullptr || out_flags == nullptr ||
            (out_positions == nullptr && capacity > 0)) {
            return null_argument(sim == nullptr ? "sim" : (request == nullptr ? "request" : "an output"));
        }
        if (capacity < 0) {
            return invalid("negative capacity");
        }
        const auto r = from_c(*request);
        if (!r) {
            return invalid("unknown frame kind");
        }
        if (!r->frame.is_inertial()) {
            return invalid("orbit paths are drawn in the ICRF or ECLIPJ2000");
        }
        if (const auto valid = usim::presentation::validate(r->view); !valid) {
            return fail(valid.error());
        }
        const usim::time::Instant now = sim->sim.clock().now();
        const auto path = sim->orbits.revolution(from_c(body), from_c(orbit_center), r->frame, now);
        if (!path) {
            return fail(path.error());
        }
        // The path is relative to the orbit center; the view is relative to the request's center.
        const auto offset = sim->sim.ephemeris().evaluate(from_c(orbit_center), r->center, r->frame, now);
        if (!offset.ok()) {
            const auto detailed = sim->sim.ephemeris().state(from_c(orbit_center), r->center, r->frame, now);
            return detailed ? fail(usim::ErrorCode::internal, "inconsistent ephemeris evaluation")
                            : fail(detailed.error());
        }
        const auto& points = path.value()->points;
        const std::size_t n = std::min(points.size(), static_cast<std::size_t>(capacity));
        for (std::size_t i = 0; i < n; ++i) {
            const usim::math::Vec3 p = usim::presentation::to_render_position(
                r->view, points[i].position_km + offset.state.position_km);
            out_positions[i] =
                to_c(p * usim::presentation::distance_compression(r->view, usim::math::norm(p)));
        }
        *out_count = static_cast<std::int32_t>(points.size());
        *out_flags = (path.value()->full_revolution ? USIM_ORBIT_FULL_REVOLUTION : 0u) |
                     (path.value()->approximate || offset.approximate ? USIM_ORBIT_APPROXIMATE : 0u);
        return USIM_STATUS_OK;
    });
}

} // extern "C"

namespace {

// The index of the source with registry id `id` in the catalog's source table, or -1.
std::int32_t source_index(const usim::catalog::BodyCatalog& catalog, const std::string& id) noexcept {
    const auto sources = catalog.sources();
    for (std::size_t i = 0; i < sources.size(); ++i) {
        if (sources[i].id == id) {
            return static_cast<std::int32_t>(i);
        }
    }
    return -1;
}

} // namespace

USIM_API int32_t usim_star_count(const usim_simulation* sim, int32_t* out_count) {
    if (sim == nullptr || out_count == nullptr) {
        return null_argument(sim == nullptr ? "sim" : "out_count");
    }
    *out_count = static_cast<std::int32_t>(sim->sim.stars().stars.size());
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_star_catalog_source(const usim_simulation* sim, int32_t* out_source_index,
                                          const char** out_locator, const char** out_derivation) {
    if (sim == nullptr || out_source_index == nullptr || out_locator == nullptr || out_derivation == nullptr) {
        return null_argument(sim == nullptr ? "sim" : "out");
    }
    const usim::catalog::StarCatalog& stars = sim->sim.stars();
    if (stars.stars.empty()) {
        return fail(usim::ErrorCode::not_found, "the pack has no star catalog");
    }
    *out_source_index = source_index(sim->sim.catalog(), stars.source.source_id);
    *out_locator = stars.source.locator.c_str();
    *out_derivation = stars.derivation.c_str();
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_fill_stars(const usim_simulation* sim, const usim_snapshot_request* request, usim_star* out,
                                 int32_t capacity, int32_t* out_count) {
    return guarded([&] {
        if (sim == nullptr || request == nullptr || out_count == nullptr || (out == nullptr && capacity > 0)) {
            return null_argument(
                sim == nullptr
                    ? "sim"
                    : (request == nullptr ? "request" : (out_count == nullptr ? "out_count" : "out")));
        }
        if (capacity < 0) {
            return invalid("negative capacity");
        }
        const auto r = from_c(*request);
        if (!r) {
            return invalid("unknown frame kind");
        }
        // In chunks through a buffer on the stack, as usim_fill_snapshot: no allocation, nothing shared.
        std::array<usim::sim::StarSnapshot, 64> chunk;
        const std::size_t wanted = std::min(static_cast<std::size_t>(capacity), sim->sim.stars().stars.size());
        std::size_t done = 0;
        while (done < wanted) {
            const std::size_t n = std::min(chunk.size(), wanted - done);
            const auto filled = sim->sim.fill_stars(*r, std::span(chunk.data(), n), done);
            if (!filled) {
                return fail(filled.error());
            }
            for (std::size_t i = 0; i < filled.value(); ++i) {
                const usim::sim::StarSnapshot& s = chunk[i];
                out[done + i] = {to_c(s.direction), s.v_magnitude, s.b_minus_v, s.flags, 0};
            }
            done += filled.value();
        }
        *out_count = static_cast<std::int32_t>(done);
        return static_cast<std::int32_t>(USIM_STATUS_OK);
    });
}

USIM_API int32_t usim_ring_count(const usim_simulation* sim, int32_t* out_count) {
    if (sim == nullptr || out_count == nullptr) {
        return null_argument(sim == nullptr ? "sim" : "out_count");
    }
    *out_count = static_cast<std::int32_t>(sim->sim.rings().size());
    return USIM_STATUS_OK;
}

USIM_API int32_t usim_ring_at(const usim_simulation* sim, int32_t index, usim_ring* out) {
    if (sim == nullptr || out == nullptr) {
        return null_argument(sim == nullptr ? "sim" : "out");
    }
    const auto rings = sim->sim.rings();
    if (index < 0 || static_cast<std::size_t>(index) >= rings.size()) {
        return invalid("ring index out of range");
    }
    const usim::catalog::Ring& ring = rings[static_cast<std::size_t>(index)];
    const usim::catalog::BodyCatalog& catalog = sim->sim.catalog();
    const usim::catalog::Body* body = catalog.find(ring.body);
    const auto body_index = body == nullptr ? -1 : static_cast<std::int32_t>(body - catalog.bodies().data());
    const auto& depth = ring.optical_depth;
    *out = {body_index,
            (ring.gap ? USIM_RING_GAP : 0u) | (depth ? USIM_RING_HAS_OPTICAL_DEPTH : 0u),
            ring.inner_radius_km,
            ring.outer_radius_km,
            depth ? depth->smallest : 0.0,
            depth ? depth->largest : 0.0,
            source_index(catalog, ring.radii_source.source_id),
            depth ? source_index(catalog, depth->source.source_id) : -1,
            ring.name.c_str(),
            ring.radii_source.locator.c_str(),
            depth ? depth->source.locator.c_str() : ""};
    return USIM_STATUS_OK;
}
