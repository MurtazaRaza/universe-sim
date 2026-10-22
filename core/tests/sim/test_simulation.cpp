// P4-02: the Simulation facade on the synthetic round-trip pack (made-up values, not physical data).

#include "fixtures/test_pack_expected.hpp"
#include "usim/presentation/axis_convention.hpp"
#include "usim/sim/simulation.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cmath>
#include <filesystem>
#include <span>
#include <vector>

using usim::ErrorCode;
using usim::catalog::BodyId;
using usim::frames::FrameId;
using usim::math::Quat;
using usim::math::Vec3;
using usim::sim::BodySnapshot;
using usim::sim::Simulation;
using usim::sim::SnapshotRequest;
using usim::time::Instant;
namespace expected = usim::test::pack;

namespace {

Simulation make_simulation() {
    auto sim = Simulation::load_file(std::filesystem::path(USIM_TEST_FIXTURES_DIR) / expected::k_file_name);
    if (!sim) {
        FAIL("cannot create the simulation: " << sim.error().message);
    }
    return std::move(sim).value();
}

BodyId naif(std::int64_t id) {
    return BodyId::naif(id);
}

Instant at(double s) {
    return Instant::from_seconds_since_j2000(s);
}

} // namespace

TEST_CASE("the simulation's coverage is the span every body shares, and the clock starts in it", "[sim]") {
    const Simulation sim = make_simulation();
    // 1002 is covered by its own segment over [-86400, 86400] and needs 1001, covered from 3600 s.
    CHECK(sim.coverage().start == at(3600.0));
    CHECK(sim.coverage().end == at(86400.0));
    CHECK(sim.clock().limits() == sim.coverage());
    CHECK(sim.clock().now() == at(3600.0)); // J2000 clamped into the coverage
    CHECK(sim.catalog().size() == std::size(expected::k_bodies));
    CHECK(sim.snapshot_size() == sim.catalog().size());
}

TEST_CASE("states come from the chain, flagged when approximate", "[sim]") {
    const Simulation sim = make_simulation();
    const Instant t = at(40000.0);
    const auto s = sim.state(naif(1002), naif(0), FrameId::icrf(), t);
    REQUIRE(s.has_value());
    const auto chained = sim.ephemeris().state(naif(1002), naif(0), FrameId::icrf(), t);
    REQUIRE(chained.has_value());
    CHECK(s.value().state.position_km == chained.value().position_km);
    CHECK(s.value().body == naif(1002));
    CHECK(s.value().center == naif(0));
    CHECK(s.value().t == t);
    CHECK(s.value().quality.model == usim::sim::StateModel::ephemeris);
    CHECK_FALSE(s.value().quality.approximate);

    const auto approximate = sim.state(naif(1003), naif(0), FrameId::eclipj2000(), t);
    REQUIRE(approximate.has_value());
    CHECK(approximate.value().quality.approximate);

    CHECK(sim.state(naif(1002), naif(0), FrameId::icrf(), at(0.0)).error().code ==
          ErrorCode::out_of_coverage);
    CHECK(sim.state(naif(77), naif(0), FrameId::icrf(), t).error().code == ErrorCode::not_found);
}

TEST_CASE("states in a body-fixed frame include the frame's rotation", "[sim]") {
    const Simulation sim = make_simulation();
    const Instant t = at(50000.0);
    const FrameId fixed = FrameId::body_fixed(naif(1001));
    const auto s = sim.state(naif(1002), naif(1001), fixed, t);
    REQUIRE(s.has_value());
    const auto icrf = sim.ephemeris().state(naif(1002), naif(1001), FrameId::icrf(), t).value();
    const auto expected_state = sim.frames().transform(FrameId::icrf(), fixed, t).value().apply(icrf);
    CHECK(s.value().state.position_km == expected_state.position_km);
    CHECK(s.value().state.velocity_km_s == expected_state.velocity_km_s);
    CHECK(s.value().frame == fixed);
    // A body without a rotation model has no body-fixed frame.
    CHECK(sim.state(naif(1002), naif(1001), FrameId::body_fixed(naif(1002)), t).error().code ==
          ErrorCode::not_found);
}

TEST_CASE("orientations rotate body-fixed axes into the requested frame", "[sim]") {
    const Simulation sim = make_simulation();
    const Instant t = at(20000.0);
    const auto q = sim.orientation(naif(1001), FrameId::eclipj2000(), t);
    REQUIRE(q.has_value());
    const auto to_ecliptic =
        sim.frames().transform(FrameId::body_fixed(naif(1001)), FrameId::eclipj2000(), t).value();
    CHECK(usim::math::same_rotation(q.value(), Quat::from_matrix(to_ecliptic.rotation()), 1e-15));
    CHECK(sim.orientation(naif(1002), FrameId::icrf(), t).error().code == ErrorCode::not_found);
}

TEST_CASE("a snapshot gives render positions, orientations, radii and flags", "[sim][snapshot]") {
    Simulation sim = make_simulation();
    REQUIRE(sim.clock().set_time(at(30000.0)).has_value());
    SnapshotRequest request;
    request.center = naif(1000);
    request.frame = FrameId::eclipj2000();
    request.view.origin_km = {0.5, -0.25, 2.0};
    request.view.km_per_render_unit = 0.5;
    request.view.axes = usim::presentation::AxisConvention::unity;
    request.view.scale_mode = usim::presentation::ScaleMode::enlarged_bodies;
    request.view.body_scale = 4.0;

    std::vector<BodySnapshot> out(sim.snapshot_size());
    const auto count = sim.fill_snapshot(request, out);
    REQUIRE(count.has_value());
    REQUIRE(count.value() == sim.snapshot_size());

    for (std::size_t i = 0; i < out.size(); ++i) {
        const auto& body = sim.catalog().bodies()[i];
        const BodySnapshot& s = out[i];
        INFO(usim::catalog::to_string(body.id));
        CHECK(s.body == body.id);
        const auto e = sim.ephemeris().evaluate(body.id, request.center, request.frame, sim.clock().now());
        REQUIRE(e.ok());
        CHECK(s.render_position == usim::presentation::to_render_position(request.view, e.state.position_km));
        CHECK(((s.flags & usim::sim::snapshot_approximate) != 0) == e.approximate);
        CHECK((s.flags & usim::sim::snapshot_no_position) == 0);

        const auto q = sim.orientation(body.id, request.frame, sim.clock().now());
        CHECK(((s.flags & usim::sim::snapshot_no_orientation) != 0) == !q.has_value());
        if (q.has_value()) {
            CHECK(s.orientation == usim::presentation::to_render_rotation(request.view, q.value()));
        }
        CHECK(((s.flags & usim::sim::snapshot_no_radius) != 0) == !body.mean_radius.has_value());
        if (body.mean_radius.has_value()) {
            CHECK(s.render_radius == *body.mean_radius.value / 0.5 * 4.0);
        }
        if (body.radius_a.has_value()) {
            // Unity axes (x, z, y): b and c swap places.
            CHECK(s.render_radii.x == *body.radius_a.value / 0.5 * 4.0);
            CHECK(s.render_radii.y == *body.radius_c.value / 0.5 * 4.0);
            CHECK(s.render_radii.z == *body.radius_b.value / 0.5 * 4.0);
        } else if (body.mean_radius.has_value()) {
            CHECK(s.render_radii == Vec3{s.render_radius, s.render_radius, s.render_radius});
        } else {
            CHECK(s.render_radii == Vec3{});
        }
    }
    CHECK((out[4].flags & usim::sim::snapshot_approximate) != 0); // 1003, placed at 1000
}

TEST_CASE("snapshots light bodies from the star and compress far distances", "[sim][snapshot]") {
    Simulation sim = make_simulation();
    REQUIRE(sim.clock().set_time(at(30000.0)).has_value());
    CHECK(sim.light_source() == naif(1000)); // the synthetic catalog's star
    SnapshotRequest request;
    request.center = naif(1002);
    request.view.axes = usim::presentation::AxisConvention::unity;
    std::vector<BodySnapshot> plain(sim.snapshot_size());
    REQUIRE(sim.fill_snapshot(request, plain).has_value());

    // Light: a unit vector from each body toward the star, in host axes; none for the star itself.
    for (std::size_t i = 0; i < plain.size(); ++i) {
        const auto id = sim.catalog().bodies()[i].id;
        INFO(usim::catalog::to_string(id));
        if (id == naif(1000)) {
            CHECK((plain[i].flags & usim::sim::snapshot_no_light) != 0);
            CHECK(plain[i].light_direction == Vec3{});
            continue;
        }
        const auto toward = sim.ephemeris().evaluate(naif(1000), id, request.frame, sim.clock().now());
        REQUIRE(toward.ok());
        if (usim::math::norm(toward.state.position_km) == 0.0) {
            CHECK((plain[i].flags & usim::sim::snapshot_no_light) != 0); // placed at the star (1003)
            continue;
        }
        const Vec3 expected =
            usim::presentation::to_host(request.view.axes, usim::math::normalized(toward.state.position_km));
        CHECK(usim::math::distance(plain[i].light_direction, expected) < 1e-15);
    }

    // Compression: beyond the horizon, position and size shrink by one factor along the line of sight.
    request.view.compression_distance = 0.5;
    std::vector<BodySnapshot> compressed(sim.snapshot_size());
    REQUIRE(sim.fill_snapshot(request, compressed).has_value());
    std::size_t count = 0;
    for (std::size_t i = 0; i < plain.size(); ++i) {
        const double d = usim::math::norm(plain[i].render_position);
        const double f = usim::presentation::distance_compression(request.view, d);
        CHECK(usim::math::distance(compressed[i].render_position, plain[i].render_position * f) < 1e-12);
        CHECK(std::abs(compressed[i].render_radius - plain[i].render_radius * f) < 1e-15);
        CHECK(((compressed[i].flags & usim::sim::snapshot_compressed) != 0) == (f < 1.0));
        count += f < 1.0 ? 1 : 0;
    }
    CHECK(count > 0);
}

TEST_CASE("a snapshot fills as many bodies as fit", "[sim][snapshot]") {
    const Simulation sim = make_simulation();
    std::vector<BodySnapshot> out(2);
    const auto count = sim.fill_snapshot(SnapshotRequest{}, out);
    REQUIRE(count.has_value());
    CHECK(count.value() == 2);
    CHECK(out[1].body == sim.catalog().bodies()[1].id);
    CHECK(sim.fill_snapshot(SnapshotRequest{}, std::span<BodySnapshot>{}).value() == 0);
    // From an offset: the remaining bodies.
    std::vector<BodySnapshot> rest(10);
    const auto tail = sim.fill_snapshot(SnapshotRequest{}, rest, 3);
    REQUIRE(tail.has_value());
    CHECK(tail.value() == sim.snapshot_size() - 3);
    CHECK(rest[0].body == sim.catalog().bodies()[3].id);
    CHECK(sim.fill_snapshot(SnapshotRequest{}, rest, sim.snapshot_size()).value() == 0);
    CHECK(sim.fill_snapshot(SnapshotRequest{}, rest, sim.snapshot_size() + 1).error().code ==
          ErrorCode::invalid_argument);
}

TEST_CASE("invalid snapshot requests are rejected", "[sim][snapshot]") {
    const Simulation sim = make_simulation();
    std::vector<BodySnapshot> out(sim.snapshot_size());
    SnapshotRequest bad_view;
    bad_view.view.km_per_render_unit = 0.0;
    CHECK(sim.fill_snapshot(bad_view, out).error().code == ErrorCode::invalid_argument);
    SnapshotRequest rotating;
    rotating.frame = FrameId::body_fixed(naif(1001));
    CHECK(sim.fill_snapshot(rotating, out).error().code == ErrorCode::invalid_argument);
    SnapshotRequest unknown;
    unknown.center = naif(4242);
    CHECK(sim.fill_snapshot(unknown, out).error().code == ErrorCode::not_found);
}

TEST_CASE("loading reports pack errors", "[sim]") {
    const auto missing =
        Simulation::load_file(std::filesystem::path(USIM_TEST_FIXTURES_DIR) / "no-such-pack.uspack");
    CHECK(missing.error().code == ErrorCode::io_error);
    const std::vector<std::byte> garbage(64, std::byte{0});
    CHECK(Simulation::load(garbage).error().code == ErrorCode::data_format);
}

TEST_CASE("default orbit centers follow the catalog hierarchy", "[sim][orbit]") {
    const Simulation sim = make_simulation();
    // Synthetic catalog: root 0, star 1000, planet 1001 (primary 1000), moon 1002 (primary 1001),
    // dwarf planet 1003 (primary 1000).
    CHECK(sim.default_orbit_center(naif(1000)).value() == naif(0));
    CHECK(sim.default_orbit_center(naif(1001)).value() == naif(1000));
    CHECK(sim.default_orbit_center(naif(1003)).value() == naif(1000));
    CHECK(sim.default_orbit_center(naif(1002)).value() ==
          naif(1000)); // its primary is a planet, not a barycenter
    CHECK(sim.default_orbit_center(naif(0)).error().code == ErrorCode::not_found);
    CHECK(sim.default_orbit_center(naif(77)).error().code == ErrorCode::not_found);
}

TEST_CASE("stars are given as unit directions in the display frame and host axes", "[sim][stars]") {
    Simulation sim = make_simulation();
    REQUIRE(sim.clock().set_time(at(1.0e8)).has_value());
    const auto& catalog = sim.stars();
    REQUIRE(catalog.stars.size() == std::size(expected::k_stars));
    std::vector<usim::sim::StarSnapshot> out(catalog.stars.size());

    SnapshotRequest icrf;
    icrf.frame = FrameId::icrf();
    REQUIRE(sim.fill_stars(icrf, out).value() == out.size());
    for (std::size_t i = 0; i < out.size(); ++i) {
        const Vec3 expected_direction =
            usim::catalog::star_direction_icrf(catalog.stars[i], catalog.epoch_tdb_s,
                                               sim.clock().now().seconds_since_j2000());
        CHECK(usim::math::distance(out[i].direction, expected_direction) < 1e-15); // core axes, ICRF
        CHECK(out[i].v_magnitude == expected::k_stars[i].v_magnitude);
        CHECK(out[i].b_minus_v == expected::k_stars[i].b_minus_v);
        CHECK(((out[i].flags & usim::sim::star_has_color) != 0) == expected::k_stars[i].has_color);
    }

    // ECLIPJ2000 in Unity axes: the same directions, rotated and mapped.
    SnapshotRequest unity;
    unity.view.axes = usim::presentation::AxisConvention::unity;
    std::vector<usim::sim::StarSnapshot> mapped(out.size());
    REQUIRE(sim.fill_stars(unity, mapped).has_value());
    const auto rotation = sim.frames().try_transform(FrameId::icrf(), FrameId::eclipj2000(), sim.clock().now());
    REQUIRE(rotation.has_value());
    for (std::size_t i = 0; i < out.size(); ++i) {
        const Vec3 e = usim::presentation::to_host(usim::presentation::AxisConvention::unity,
                                                   rotation->rotation() * out[i].direction);
        CHECK(usim::math::distance(mapped[i].direction, e) < 1e-15);
    }

    // Partial fills and invalid requests.
    CHECK(sim.fill_stars(icrf, std::span(out).first(1), 2).value() == 1);
    CHECK(sim.fill_stars(icrf, out, out.size() + 1).error().code == ErrorCode::invalid_argument);
    SnapshotRequest rotating;
    rotating.frame = FrameId::body_fixed(naif(1001));
    CHECK(sim.fill_stars(rotating, out).error().code == ErrorCode::invalid_argument);
}

TEST_CASE("the simulation gives the pack's rings", "[sim][rings]") {
    const Simulation sim = make_simulation();
    REQUIRE(sim.rings().size() == std::size(expected::k_rings));
    CHECK(sim.rings()[1].name == expected::k_rings[1].name);
}

