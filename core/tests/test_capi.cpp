// The C ABI (P4-06), through the exported functions of the shared library, on the synthetic round-trip
// pack and, where present, the default pack.

#include "fixtures/test_pack_expected.hpp"
#include "usim/result.hpp"
#include "usim/sim/simulation.hpp"
#include "usim/usim.h"
#include "usim/version.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace expected = usim::test::pack;

namespace {

std::string fixture_path() {
    return (std::filesystem::path(USIM_TEST_FIXTURES_DIR) / expected::k_file_name).string();
}

// A simulation handle, destroyed at the end of the scope.
struct Handle {
    usim_simulation* sim = nullptr;
    Handle() {
        REQUIRE(usim_simulation_load_file(fixture_path().c_str(), &sim) == USIM_STATUS_OK);
        REQUIRE(sim != nullptr);
    }
    ~Handle() { usim_simulation_destroy(sim); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};

usim_body_id naif(std::int64_t id) {
    return {USIM_ID_NAMESPACE_NAIF, 0, id};
}

usim_frame frame(std::int32_t kind, std::int64_t body = 0) {
    return {kind, 0, body == 0 ? usim_body_id{0, 0, 0} : naif(body)};
}

usim_instant at(double s) {
    usim_instant t{};
    REQUIRE(usim_instant_from_seconds(s, &t) == USIM_STATUS_OK);
    return t;
}

usim::sim::Simulation cpp_simulation() {
    auto sim = usim::sim::Simulation::load_file(fixture_path());
    REQUIRE(sim.has_value());
    return std::move(sim).value();
}

} // namespace

TEST_CASE("C ABI reports the ABI version of its header", "[capi]") {
    CHECK(usim_abi_version() == USIM_ABI_VERSION);
    CHECK(USIM_ABI_VERSION == 3u);
}

TEST_CASE("C ABI version string matches the core", "[capi]") {
    const char* version = usim_version_string();
    REQUIRE(version != nullptr);
    CHECK(std::string_view{version} == usim::version_string());
}

TEST_CASE("C ABI status names match the core error codes", "[capi]") {
    CHECK(std::string_view{usim_status_name(USIM_STATUS_OK)} == "ok");
    for (std::int32_t status = 1; status <= usim::k_max_error_code; ++status) {
        CHECK(std::string_view{usim_status_name(status)} ==
              usim::to_string(static_cast<usim::ErrorCode>(status)));
    }
    CHECK(std::string_view{usim_status_name(-1)} == "unknown");
    CHECK(std::string_view{usim_status_name(usim::k_max_error_code + 1)} == "unknown");
    CHECK(std::string_view{usim_status_name(USIM_STATUS_OUT_OF_COVERAGE)} == "out_of_coverage");
}

TEST_CASE("C ABI struct sizes are reported for binding checks", "[capi]") {
    CHECK(usim_struct_size(USIM_STRUCT_VEC3D) == sizeof(usim_vec3d));
    CHECK(usim_struct_size(USIM_STRUCT_QUATD) == sizeof(usim_quatd));
    CHECK(usim_struct_size(USIM_STRUCT_BODY_ID) == 16);
    CHECK(usim_struct_size(USIM_STRUCT_INSTANT) == 16);
    CHECK(usim_struct_size(USIM_STRUCT_FRAME) == 24);
    CHECK(usim_struct_size(USIM_STRUCT_BODY_INFO) == sizeof(usim_body_info));
    CHECK(usim_struct_size(USIM_STRUCT_QUANTITY) == sizeof(usim_quantity));
    CHECK(usim_struct_size(USIM_STRUCT_SOURCE) == sizeof(usim_source));
    CHECK(usim_struct_size(USIM_STRUCT_CLOCK_STATE) == sizeof(usim_clock_state));
    CHECK(usim_struct_size(USIM_STRUCT_STATE) == sizeof(usim_state));
    CHECK(usim_struct_size(USIM_STRUCT_SNAPSHOT_REQUEST) == sizeof(usim_snapshot_request));
    CHECK(usim_struct_size(USIM_STRUCT_BODY_SNAPSHOT) == sizeof(usim_body_snapshot));
    CHECK(usim_struct_size(USIM_STRUCT_COVERAGE) == sizeof(usim_coverage));
    CHECK(usim_struct_size(USIM_STRUCT_STAR) == 48);
    CHECK(usim_struct_size(USIM_STRUCT_RING) == sizeof(usim_ring));
    CHECK(usim_struct_size(0) == 0);
    CHECK(usim_struct_size(999) == 0);
}

TEST_CASE("C ABI loading reports errors with a message", "[capi]") {
    usim_simulation* sim = nullptr;
    CHECK(usim_simulation_load_file("/no/such/pack.uspack", &sim) == USIM_STATUS_IO_ERROR);
    CHECK(sim == nullptr);
    CHECK(std::string_view{usim_last_error_message()}.find("pack.uspack") != std::string_view::npos);
    CHECK(usim_simulation_load_file(nullptr, &sim) == USIM_STATUS_INVALID_ARGUMENT);
    CHECK(std::string_view{usim_last_error_message()} == "argument utf8_path is null");
    CHECK(usim_simulation_load_file(fixture_path().c_str(), nullptr) == USIM_STATUS_INVALID_ARGUMENT);
    const char garbage[16] = {};
    CHECK(usim_simulation_load_memory(garbage, sizeof garbage, &sim) == USIM_STATUS_DATA_FORMAT);
    usim_simulation_destroy(nullptr); // ignored
}

TEST_CASE("C ABI error messages are per thread", "[capi]") {
    usim_simulation* sim = nullptr;
    REQUIRE(usim_simulation_load_file(nullptr, &sim) == USIM_STATUS_INVALID_ARGUMENT);
    std::string other;
    std::thread([&] { other = usim_last_error_message(); }).join();
    CHECK(other.empty());
    CHECK(std::string_view{usim_last_error_message()} == "argument utf8_path is null");
}

TEST_CASE("C ABI loads a pack from memory", "[capi]") {
    std::ifstream in(fixture_path(), std::ios::binary);
    const std::vector<char> bytes((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
    usim_simulation* sim = nullptr;
    REQUIRE(usim_simulation_load_memory(bytes.data(), bytes.size(), &sim) == USIM_STATUS_OK);
    std::int32_t count = 0;
    REQUIRE(usim_body_count(sim, &count) == USIM_STATUS_OK);
    CHECK(count == static_cast<std::int32_t>(std::size(expected::k_bodies)));
    usim_simulation_destroy(sim);
}

TEST_CASE("C ABI catalog queries", "[capi]") {
    Handle h;
    std::int32_t count = 0;
    REQUIRE(usim_body_count(h.sim, &count) == USIM_STATUS_OK);
    REQUIRE(count == static_cast<std::int32_t>(std::size(expected::k_bodies)));
    for (std::int32_t i = 0; i < count; ++i) {
        usim_body_info info{};
        REQUIRE(usim_body_info_at(h.sim, i, &info) == USIM_STATUS_OK);
        const auto& e = expected::k_bodies[i];
        CHECK(info.id.id_namespace == USIM_ID_NAMESPACE_NAIF);
        CHECK(info.id.value == e.naif_id);
        CHECK(std::string_view{info.name} == e.name);
        CHECK(info.kind == e.kind);
        CHECK((info.flags & USIM_BODY_HAS_EPHEMERIS) != 0);
        std::int32_t found = -1;
        REQUIRE(usim_body_find(h.sim, info.id, &found) == USIM_STATUS_OK);
        CHECK(found == i);
    }
    usim_body_info planet{};
    REQUIRE(usim_body_info_at(h.sim, 2, &planet) == USIM_STATUS_OK); // 1001, with radii
    CHECK((planet.flags & (USIM_BODY_HAS_RADII | USIM_BODY_HAS_MEAN_RADIUS | USIM_BODY_HAS_ROTATION)) ==
          (USIM_BODY_HAS_RADII | USIM_BODY_HAS_MEAN_RADIUS | USIM_BODY_HAS_ROTATION));
    CHECK(planet.primary.value == 1000);
    usim_body_info approximated{};
    REQUIRE(usim_body_info_at(h.sim, 4, &approximated) == USIM_STATUS_OK); // 1003
    CHECK((approximated.flags & USIM_BODY_APPROXIMATE) != 0);
    const char* reason = nullptr;
    REQUIRE(usim_body_approximation(h.sim, 4, &reason) == USIM_STATUS_OK);
    CHECK(std::string_view{reason} == expected::k_approximations[0].reason);
    REQUIRE(usim_body_approximation(h.sim, 2, &reason) == USIM_STATUS_OK);
    CHECK(std::string_view{reason}.empty());

    usim_quantity q{};
    REQUIRE(usim_body_quantity(h.sim, 2, USIM_FIELD_MEAN_RADIUS, &q) == USIM_STATUS_OK);
    CHECK(q.has_value == 1);
    CHECK(q.unit == USIM_UNIT_KM);
    CHECK(q.value == planet.mean_radius_km);
    CHECK(std::string_view{q.derivation}.empty() == false);
    REQUIRE(usim_body_quantity(h.sim, 3, USIM_FIELD_GM, &q) == USIM_STATUS_OK); // 1002, with uncertainty
    CHECK(q.has_uncertainty == 1);
    CHECK(std::string_view{q.source_id} == "S-TEST-A");
    REQUIRE(usim_body_quantity(h.sim, 0, USIM_FIELD_GM, &q) == USIM_STATUS_OK);
    CHECK(q.has_value == 0);
    CHECK(usim_body_quantity(h.sim, 0, 42, &q) == USIM_STATUS_INVALID_ARGUMENT);
    CHECK(usim_body_info_at(h.sim, count, &planet) == USIM_STATUS_INVALID_ARGUMENT);
    std::int32_t index = 0;
    CHECK(usim_body_find(h.sim, naif(4242), &index) == USIM_STATUS_NOT_FOUND);

    std::int32_t sources = 0;
    REQUIRE(usim_source_count(h.sim, &sources) == USIM_STATUS_OK);
    REQUIRE(sources == static_cast<std::int32_t>(std::size(expected::k_sources)));
    usim_source source{};
    REQUIRE(usim_source_at(h.sim, 1, &source) == USIM_STATUS_OK);
    CHECK(std::string_view{source.id} == expected::k_sources[1].id);
    CHECK(source.tier == expected::k_sources[1].tier);
    CHECK(usim_source_at(h.sim, sources, &source) == USIM_STATUS_INVALID_ARGUMENT);
}

TEST_CASE("C ABI time conversions", "[capi]") {
    Handle h;
    usim_instant t{};
    REQUIRE(usim_instant_make(1, -3600.0, &t) == USIM_STATUS_OK);
    CHECK(t.days == 0);
    CHECK(t.seconds == 82800.0);
    CHECK(usim_instant_seconds_between(at(0.0), t) == 82800.0);
    CHECK(usim_instant_make(0, 1.0 / 0.0, &t) == USIM_STATUS_INVALID_ARGUMENT);

    REQUIRE(usim_time_from_fields(h.sim, USIM_SCALE_TDB, 2000, 1, 1, 12, 0, 0.0, &t) == USIM_STATUS_OK);
    CHECK((t.days == 0 && t.seconds == 0.0));
    CHECK(usim_time_from_fields(h.sim, USIM_SCALE_UTC, 1960, 1, 1, 0, 0, 0.0, &t) ==
          USIM_STATUS_OUT_OF_RANGE);
    CHECK(usim_time_from_fields(h.sim, 9, 2000, 1, 1, 0, 0, 0.0, &t) == USIM_STATUS_INVALID_ARGUMENT);

    REQUIRE(usim_time_parse(h.sim, "2000-01-02T12:00:00 TDB", USIM_CALENDAR_MIXED, &t) == USIM_STATUS_OK);
    CHECK((t.days == 1 && t.seconds == 0.0));
    CHECK(usim_time_parse(h.sim, "not a time", USIM_CALENDAR_MIXED, &t) == USIM_STATUS_PARSE_ERROR);

    char buffer[64];
    std::int32_t length = 0;
    REQUIRE(usim_time_format(h.sim, t, USIM_SCALE_TDB, USIM_CALENDAR_GREGORIAN, 3, buffer, sizeof buffer,
                             &length) == USIM_STATUS_OK);
    CHECK(std::string_view{buffer} == "2000-01-02T12:00:00.000 TDB");
    CHECK(length == 27);
    char small[8];
    REQUIRE(usim_time_format(h.sim, t, USIM_SCALE_TDB, USIM_CALENDAR_GREGORIAN, 3, small, sizeof small,
                             &length) == USIM_STATUS_OK);
    CHECK(length == 27);
    CHECK(std::string_view{small} == "2000-01");
    REQUIRE(usim_time_format(h.sim, t, USIM_SCALE_TDB, USIM_CALENDAR_GREGORIAN, 0, nullptr, 0, &length) ==
            USIM_STATUS_OK);
    CHECK(length == 23);
    CHECK(usim_time_format(h.sim, t, USIM_SCALE_TDB, USIM_CALENDAR_GREGORIAN, 12, buffer, sizeof buffer,
                           &length) == USIM_STATUS_INVALID_ARGUMENT);
    const usim_instant bad{0, 90000.0};
    CHECK(usim_time_format(h.sim, bad, USIM_SCALE_TDB, USIM_CALENDAR_GREGORIAN, 3, buffer, sizeof buffer,
                           &length) == USIM_STATUS_INVALID_ARGUMENT);
}

TEST_CASE("C ABI clock", "[capi]") {
    Handle h;
    usim_clock_state c{};
    REQUIRE(usim_clock_get(h.sim, &c) == USIM_STATUS_OK);
    usim_coverage coverage{};
    REQUIRE(usim_simulation_coverage(h.sim, &coverage) == USIM_STATUS_OK);
    CHECK((c.now.days == coverage.start.days && c.now.seconds == coverage.start.seconds)); // J2000 clamped
    CHECK(c.rate == 1.0);
    CHECK(c.paused == 0);
    CHECK(usim_instant_seconds_between(c.limits.start, coverage.start) == 0.0);

    REQUIRE(usim_clock_set_rate(h.sim, 100.0) == USIM_STATUS_OK);
    REQUIRE(usim_clock_advance(h.sim, 2.0) == USIM_STATUS_OK);
    REQUIRE(usim_clock_get(h.sim, &c) == USIM_STATUS_OK);
    CHECK(usim_instant_seconds_between(coverage.start, c.now) == 200.0);
    REQUIRE(usim_clock_set_paused(h.sim, 1) == USIM_STATUS_OK);
    REQUIRE(usim_clock_advance(h.sim, 2.0) == USIM_STATUS_OK);
    REQUIRE(usim_clock_get(h.sim, &c) == USIM_STATUS_OK);
    CHECK(usim_instant_seconds_between(coverage.start, c.now) == 200.0);
    CHECK(c.paused == 1);
    REQUIRE(usim_clock_set_time(h.sim, at(1.0e9)) == USIM_STATUS_OK);
    REQUIRE(usim_clock_get(h.sim, &c) == USIM_STATUS_OK);
    CHECK(c.limit == USIM_CLOCK_LIMIT_END);
    CHECK(usim_clock_advance(h.sim, -1.0) == USIM_STATUS_INVALID_ARGUMENT);
    CHECK(usim_clock_set_rate(h.sim, 1.0 / 0.0) == USIM_STATUS_INVALID_ARGUMENT);
    CHECK(usim_clock_set_time(h.sim, usim_instant{0, -5.0}) == USIM_STATUS_INVALID_ARGUMENT);
}

TEST_CASE("C ABI states and orientations equal the C++ facade", "[capi]") {
    Handle h;
    const auto sim = cpp_simulation();
    const usim_instant t = at(40000.0);
    const auto instant = usim::time::Instant::from_seconds_since_j2000(40000.0);

    usim_state s{};
    REQUIRE(usim_state_at(h.sim, naif(1002), naif(0), frame(USIM_FRAME_ECLIPJ2000), t, &s) == USIM_STATUS_OK);
    const auto e = sim.state(usim::catalog::BodyId::naif(1002), usim::catalog::BodyId::naif(0),
                             usim::frames::FrameId::eclipj2000(), instant);
    REQUIRE(e.has_value());
    CHECK(s.position_km.x == e.value().state.position_km.x);
    CHECK(s.velocity_km_s.z == e.value().state.velocity_km_s.z);
    CHECK(s.model == USIM_MODEL_EPHEMERIS);
    CHECK(s.flags == 0);
    REQUIRE(usim_state_at(h.sim, naif(1003), naif(0), frame(USIM_FRAME_ICRF), t, &s) == USIM_STATUS_OK);
    CHECK(s.flags == USIM_STATE_APPROXIMATE);
    REQUIRE(usim_state_at(h.sim, naif(1002), naif(1001), frame(USIM_FRAME_BODY_FIXED, 1001), t, &s) ==
            USIM_STATUS_OK);
    CHECK(usim_state_at(h.sim, naif(1002), naif(0), frame(USIM_FRAME_ICRF), at(0.0), &s) ==
          USIM_STATUS_OUT_OF_COVERAGE);
    CHECK(std::string_view{usim_last_error_message()}.find("naif:1001") != std::string_view::npos);
    CHECK(usim_state_at(h.sim, naif(1002), naif(0), frame(7), t, &s) == USIM_STATUS_INVALID_ARGUMENT);

    usim_quatd q{};
    REQUIRE(usim_orientation_at(h.sim, naif(1001), frame(USIM_FRAME_ICRF), t, &q) == USIM_STATUS_OK);
    const auto expected_q =
        sim.orientation(usim::catalog::BodyId::naif(1001), usim::frames::FrameId::icrf(), instant);
    REQUIRE(expected_q.has_value());
    CHECK((q.w == expected_q.value().w && q.x == expected_q.value().x && q.z == expected_q.value().z));
    CHECK(usim_orientation_at(h.sim, naif(1002), frame(USIM_FRAME_ICRF), t, &q) == USIM_STATUS_NOT_FOUND);
}

TEST_CASE("C ABI snapshots equal the C++ facade", "[capi]") {
    Handle h;
    auto sim = cpp_simulation();
    REQUIRE(usim_clock_set_time(h.sim, at(30000.0)) == USIM_STATUS_OK);
    REQUIRE(sim.clock().set_time(usim::time::Instant::from_seconds_since_j2000(30000.0)).has_value());

    usim_snapshot_request request{};
    request.center = naif(1000);
    request.frame = frame(USIM_FRAME_ECLIPJ2000);
    request.origin_km = {0.5, 0.25, -1.0};
    request.km_per_render_unit = 2.0;
    request.axes = USIM_AXES_UNITY;
    request.scale_mode = USIM_SCALE_MODE_ENLARGED;
    request.body_scale = 3.0;
    usim::sim::SnapshotRequest cpp_request;
    cpp_request.center = usim::catalog::BodyId::naif(1000);
    cpp_request.view.origin_km = {0.5, 0.25, -1.0};
    cpp_request.view.km_per_render_unit = 2.0;
    cpp_request.view.axes = usim::presentation::AxisConvention::unity;
    cpp_request.view.scale_mode = usim::presentation::ScaleMode::enlarged_bodies;
    cpp_request.view.body_scale = 3.0;

    std::vector<usim_body_snapshot> out(8);
    std::int32_t count = 0;
    REQUIRE(usim_fill_snapshot(h.sim, &request, out.data(), static_cast<std::int32_t>(out.size()), &count) ==
            USIM_STATUS_OK);
    std::vector<usim::sim::BodySnapshot> cpp(sim.snapshot_size());
    REQUIRE(sim.fill_snapshot(cpp_request, cpp).has_value());
    REQUIRE(count == static_cast<std::int32_t>(cpp.size()));
    for (std::size_t i = 0; i < cpp.size(); ++i) {
        CHECK(out[i].id.value == cpp[i].body.value);
        CHECK(out[i].render_position.x == cpp[i].render_position.x);
        CHECK(out[i].render_position.y == cpp[i].render_position.y);
        CHECK(out[i].orientation.w == cpp[i].orientation.w);
        CHECK(out[i].render_radius == cpp[i].render_radius);
        CHECK(out[i].render_radii.y == cpp[i].render_radii.y);
        CHECK(out[i].light_direction.z == cpp[i].light_direction.z);
        CHECK(out[i].flags == cpp[i].flags);
    }
    REQUIRE(usim_fill_snapshot(h.sim, &request, out.data(), 2, &count) == USIM_STATUS_OK);
    CHECK(count == 2);
    REQUIRE(usim_fill_snapshot(h.sim, &request, nullptr, 0, &count) == USIM_STATUS_OK);
    CHECK(count == 0);
    request.km_per_render_unit = 0.0;
    CHECK(usim_fill_snapshot(h.sim, &request, out.data(), 8, &count) == USIM_STATUS_INVALID_ARGUMENT);
    request.km_per_render_unit = 1.0;
    request.center = naif(4242);
    CHECK(usim_fill_snapshot(h.sim, &request, out.data(), 8, &count) == USIM_STATUS_NOT_FOUND);
}

TEST_CASE("C ABI maps vectors between core and host axes", "[capi]") {
    usim_vec3d host{};
    REQUIRE(usim_axes_to_host(USIM_AXES_UNITY, usim_vec3d{1.0, 2.0, 3.0}, &host) == USIM_STATUS_OK);
    CHECK((host.x == 1.0 && host.y == 3.0 && host.z == 2.0));
    usim_vec3d back{};
    for (const std::int32_t axes : {USIM_AXES_CORE, USIM_AXES_UNITY, USIM_AXES_GODOT, USIM_AXES_UNREAL}) {
        REQUIRE(usim_axes_to_host(axes, usim_vec3d{1.5, -2.0, 4.0}, &host) == USIM_STATUS_OK);
        REQUIRE(usim_axes_from_host(axes, host, &back) == USIM_STATUS_OK);
        CHECK((back.x == 1.5 && back.y == -2.0 && back.z == 4.0));
    }
    CHECK(usim_axes_to_host(9, host, &back) == USIM_STATUS_INVALID_ARGUMENT);
    CHECK(usim_axes_from_host(USIM_AXES_UNITY, host, nullptr) == USIM_STATUS_INVALID_ARGUMENT);
}

TEST_CASE("C ABI orbit paths are cached render positions", "[capi]") {
    Handle h;
    auto sim = cpp_simulation();
    REQUIRE(usim_clock_set_time(h.sim, at(40000.0)) == USIM_STATUS_OK);
    usim_body_id center{};
    REQUIRE(usim_default_orbit_center(h.sim, naif(1001), &center) == USIM_STATUS_OK);
    CHECK(center.value == 1000);
    CHECK(usim_default_orbit_center(h.sim, naif(0), &center) == USIM_STATUS_NOT_FOUND);

    usim_snapshot_request request{};
    request.center = naif(1001); // the camera at the planet, the orbit drawn around the star
    request.frame = frame(USIM_FRAME_ECLIPJ2000);
    request.km_per_render_unit = 1.0;
    request.body_scale = 1.0;
    std::int32_t count = 0;
    std::uint32_t flags = 0;
    REQUIRE(usim_orbit_path(h.sim, naif(1001), naif(1000), &request, nullptr, 0, &count, &flags) ==
            USIM_STATUS_OK);
    REQUIRE(count > 2);
    std::vector<usim_vec3d> points(static_cast<std::size_t>(count));
    REQUIRE(usim_orbit_path(h.sim, naif(1001), naif(1000), &request, points.data(), count, &count, &flags) ==
            USIM_STATUS_OK);

    // The same path from the C++ API, shifted from the star to the planet.
    const auto instant = usim::time::Instant::from_seconds_since_j2000(40000.0);
    const auto path = sim.orbit_path(usim::catalog::BodyId::naif(1001), usim::catalog::BodyId::naif(1000),
                                     usim::frames::FrameId::eclipj2000(), instant);
    REQUIRE(path.has_value());
    REQUIRE(path.value().points.size() == static_cast<std::size_t>(count));
    const auto offset =
        sim.ephemeris().evaluate(usim::catalog::BodyId::naif(1000), usim::catalog::BodyId::naif(1001),
                                 usim::frames::FrameId::eclipj2000(), instant);
    REQUIRE(offset.ok());
    for (std::size_t i = 0; i < points.size(); ++i) {
        const auto p = path.value().points[i].position_km + offset.state.position_km;
        CHECK(points[i].x == p.x);
        CHECK(points[i].y == p.y);
    }
    CHECK((flags & USIM_ORBIT_APPROXIMATE) == 0);
    request.frame = frame(USIM_FRAME_BODY_FIXED, 1001);
    CHECK(usim_orbit_path(h.sim, naif(1001), naif(1000), &request, points.data(), count, &count, &flags) ==
          USIM_STATUS_INVALID_ARGUMENT);
}

TEST_CASE("C ABI stars equal the C++ facade", "[capi][stars]") {
    Handle h;
    auto sim = cpp_simulation();
    REQUIRE(usim_clock_set_time(h.sim, at(2.0e8)) == USIM_STATUS_OK);
    REQUIRE(sim.clock().set_time(usim::time::Instant::from_seconds_since_j2000(2.0e8)).has_value());

    std::int32_t count = 0;
    REQUIRE(usim_star_count(h.sim, &count) == USIM_STATUS_OK);
    REQUIRE(count == static_cast<std::int32_t>(std::size(expected::k_stars)));
    std::int32_t source = -1;
    const char* locator = nullptr;
    const char* derivation = nullptr;
    REQUIRE(usim_star_catalog_source(h.sim, &source, &locator, &derivation) == USIM_STATUS_OK);
    usim_source s{};
    REQUIRE(usim_source_at(h.sim, source, &s) == USIM_STATUS_OK);
    CHECK(std::string(s.id) == expected::k_star_source_id);
    CHECK(std::string(locator) == expected::k_star_locator);
    CHECK(std::string(derivation) == expected::k_star_derivation);

    usim_snapshot_request request{};
    request.center = naif(1000);
    request.frame = frame(USIM_FRAME_ECLIPJ2000);
    request.km_per_render_unit = 1.0;
    request.axes = USIM_AXES_UNITY;
    request.body_scale = 1.0;
    usim::sim::SnapshotRequest cpp_request;
    cpp_request.view.axes = usim::presentation::AxisConvention::unity;

    std::vector<usim_star> out(static_cast<std::size_t>(count));
    std::int32_t written = 0;
    REQUIRE(usim_fill_stars(h.sim, &request, out.data(), count, &written) == USIM_STATUS_OK);
    REQUIRE(written == count);
    std::vector<usim::sim::StarSnapshot> cpp(out.size());
    REQUIRE(sim.fill_stars(cpp_request, cpp).has_value());
    for (std::size_t i = 0; i < out.size(); ++i) {
        CHECK(out[i].direction.x == cpp[i].direction.x);
        CHECK(out[i].direction.y == cpp[i].direction.y);
        CHECK(out[i].direction.z == cpp[i].direction.z);
        CHECK(out[i].v_magnitude == cpp[i].v_magnitude);
        CHECK(out[i].b_minus_v == cpp[i].b_minus_v);
        CHECK(out[i].flags == cpp[i].flags);
    }
    REQUIRE(usim_fill_stars(h.sim, &request, out.data(), 1, &written) == USIM_STATUS_OK);
    CHECK(written == 1);
    CHECK(usim_fill_stars(h.sim, &request, out.data(), -1, &written) == USIM_STATUS_INVALID_ARGUMENT);
    CHECK(usim_fill_stars(h.sim, nullptr, out.data(), 1, &written) == USIM_STATUS_INVALID_ARGUMENT);
}

TEST_CASE("C ABI rings with their sources", "[capi][rings]") {
    Handle h;
    std::int32_t count = 0;
    REQUIRE(usim_ring_count(h.sim, &count) == USIM_STATUS_OK);
    REQUIRE(count == static_cast<std::int32_t>(std::size(expected::k_rings)));
    for (std::int32_t i = 0; i < count; ++i) {
        const auto& e = expected::k_rings[static_cast<std::size_t>(i)];
        usim_ring ring{};
        REQUIRE(usim_ring_at(h.sim, i, &ring) == USIM_STATUS_OK);
        usim_body_info body{};
        REQUIRE(usim_body_info_at(h.sim, ring.body_index, &body) == USIM_STATUS_OK);
        CHECK(body.id.value == e.naif_id);
        CHECK(std::string(ring.name) == e.name);
        CHECK(((ring.flags & USIM_RING_GAP) != 0) == e.gap);
        CHECK(ring.inner_radius_km == e.inner_km);
        CHECK(ring.outer_radius_km == e.outer_km);
        usim_source s{};
        REQUIRE(usim_source_at(h.sim, ring.radii_source_index, &s) == USIM_STATUS_OK);
        CHECK(std::string(s.id) == e.radii_source_id);
        CHECK(std::string(ring.radii_locator) == e.radii_locator);
        CHECK(((ring.flags & USIM_RING_HAS_OPTICAL_DEPTH) != 0) == e.has_optical_depth);
        if (e.has_optical_depth) {
            CHECK(ring.optical_depth_smallest == e.optical_depth_smallest);
            CHECK(ring.optical_depth_largest == e.optical_depth_largest);
            REQUIRE(usim_source_at(h.sim, ring.optical_depth_source_index, &s) == USIM_STATUS_OK);
            CHECK(std::string(s.id) == e.optical_depth_source_id);
        } else {
            CHECK(ring.optical_depth_source_index == -1);
            CHECK(std::string(ring.optical_depth_locator).empty());
        }
    }
    usim_ring ring{};
    CHECK(usim_ring_at(h.sim, count, &ring) == USIM_STATUS_INVALID_ARGUMENT);
}

