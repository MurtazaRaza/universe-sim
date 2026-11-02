// ADR-0016: transfer estimates, surface sites and day lengths on the synthetic round-trip pack (made-up
// values, not physical data). The default pack's checks are in validation/test_exploration.cpp.

#include "fixtures/test_pack_expected.hpp"
#include "test_support.hpp"
#include "usim/constants.hpp"
#include "usim/math/mat3.hpp"
#include "usim/math/quat.hpp"
#include "usim/sim/exploration.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>
#include <filesystem>
#include <numbers>

using Catch::Matchers::WithinAbs;
using Catch::Matchers::WithinRel;
using usim::ErrorCode;
using usim::catalog::BodyId;
using usim::frames::FrameId;
using usim::math::Vec3;
using usim::presentation::AxisConvention;
using usim::sim::Simulation;
using usim::time::Instant;
namespace expected = usim::test::pack;
namespace sim = usim::sim;

namespace {

Simulation make_simulation() {
    auto created =
        Simulation::load_file(std::filesystem::path(USIM_TEST_FIXTURES_DIR) / expected::k_file_name);
    if (!created) {
        FAIL("cannot create the simulation: " << created.error().message);
    }
    return std::move(created).value();
}

BodyId naif(std::int64_t id) {
    return BodyId::naif(id);
}

// Inside the planet's ephemeris segment (3600 to 150000 s) and its orientation model's span.
constexpr double k_t_s = 43200.0;
const Instant k_t = Instant::from_seconds_since_j2000(k_t_s);
const BodyId k_star = naif(1000);
const BodyId k_planet = naif(1001); // triaxial radii and a binary PCK model, no GM
const BodyId k_moon = naif(1002);   // GM, no radius

usim::surface::Ellipsoid planet_ellipsoid(const Simulation& s) {
    const auto* planet = s.catalog().find(k_planet);
    return {*planet->radius_a.value, *planet->radius_b.value, *planet->radius_c.value};
}

// Rotation from the planet's body-fixed frame to `frame` at k_t.
usim::math::Mat3 planet_rotation(const Simulation& s, FrameId frame) {
    return usim::math::to_matrix(s.orientation(k_planet, frame, k_t).value());
}

} // namespace

TEST_CASE("a surface site lies on the body's reference ellipsoid in its body-fixed frame", "[exploration]") {
    const Simulation s = make_simulation();
    const usim::surface::PlanetocentricPoint point{0.3, -1.2, 0.5};
    const auto site = sim::surface_site(s, k_planet, point, FrameId::eclipj2000(), AxisConvention::core, k_t);
    REQUIRE(site.has_value());

    const auto e = planet_ellipsoid(s);
    const auto rotation = planet_rotation(s, FrameId::eclipj2000());
    const auto horizon = usim::surface::local_frame(e, point.latitude_rad, point.longitude_rad);
    CHECK(usim::test::max_abs_difference(site.value().position_km,
                                         rotation * usim::surface::position_km(e, point)) < 1e-13);
    CHECK(usim::test::max_abs_difference(site.value().up, rotation * horizon.up) < 1e-15);
    CHECK(usim::test::max_abs_difference(site.value().north, rotation * horizon.north) < 1e-15);
    CHECK(usim::test::max_abs_difference(site.value().east, rotation * horizon.east) < 1e-15);
    CHECK_THAT(site.value().surface_radius_km,
               WithinRel(usim::surface::radius_along(e, usim::surface::direction(0.3, -1.2)), 1e-15));

    // The same site for a host: every vector mapped to its axes.
    const auto unity =
        sim::surface_site(s, k_planet, point, FrameId::eclipj2000(), AxisConvention::unity, k_t);
    REQUIRE(unity.has_value());
    CHECK(unity.value().position_km ==
          usim::presentation::to_host(AxisConvention::unity, site.value().position_km));
    CHECK(unity.value().north == usim::presentation::to_host(AxisConvention::unity, site.value().north));
    CHECK(unity.value().sun_direction ==
          usim::presentation::to_host(AxisConvention::unity, site.value().sun_direction));
    CHECK(unity.value().sun_elevation_rad == site.value().sun_elevation_rad);
}

TEST_CASE("the site below a position gives back that position's coordinates", "[exploration]") {
    const Simulation s = make_simulation();
    usim::test::SplitMix64 rng(0x16);
    for (int k = 0; k < 200; ++k) {
        const usim::surface::PlanetocentricPoint point{rng.uniform(-1.5, 1.5), rng.uniform(-3.1, 3.1),
                                                       rng.uniform(-0.5, 20.0)};
        const auto site = sim::surface_site(s, k_planet, point, FrameId::icrf(), AxisConvention::unity, k_t);
        REQUIRE(site.has_value());
        const auto below = sim::surface_site_below(s, k_planet, site.value().position_km, FrameId::icrf(),
                                                   AxisConvention::unity, k_t);
        REQUIRE(below.has_value());
        CHECK_THAT(below.value().point.latitude_rad, WithinAbs(point.latitude_rad, 1e-13));
        CHECK_THAT(below.value().point.longitude_rad, WithinAbs(point.longitude_rad, 1e-13));
        CHECK_THAT(below.value().point.altitude_km, WithinAbs(point.altitude_km, 1e-12));
    }
}

TEST_CASE("the Sun's elevation and azimuth place it in the local horizon", "[exploration]") {
    const Simulation s = make_simulation();
    const auto site =
        sim::surface_site(s, k_planet, {-0.4, 2.0, 0.0}, FrameId::eclipj2000(), AxisConvention::core, k_t);
    REQUIRE(site.has_value());
    const auto& v = site.value();
    REQUIRE((v.flags & sim::site_has_sun) != 0u);
    const Vec3 rebuilt = (v.north * std::cos(v.sun_azimuth_rad) + v.east * std::sin(v.sun_azimuth_rad)) *
                             std::cos(v.sun_elevation_rad) +
                         v.up * std::sin(v.sun_elevation_rad);
    CHECK(usim::test::max_abs_difference(rebuilt, v.sun_direction) < 1e-14);
    CHECK(v.sun_azimuth_rad >= 0.0);
    CHECK(v.sun_azimuth_rad < 2.0 * std::numbers::pi);
    CHECK(v.local_solar_time_h >= 0.0);
    CHECK(v.local_solar_time_h < 24.0);

    // The star relative to the site, from the ephemeris.
    const auto star = s.state(k_star, k_planet, FrameId::eclipj2000(), k_t);
    REQUIRE(star.has_value());
    const Vec3 toward = star.value().state.position_km - v.position_km;
    CHECK_THAT(v.sun_distance_km, WithinRel(usim::math::norm(toward), 1e-15));
    const double ratio = usim::constants::k_au_km / usim::math::norm(toward);
    CHECK_THAT(v.sunlight_ratio, WithinRel(ratio * ratio, 1e-14));

    // The synthetic star has no radius and the planet no GM: those values are absent, not invented.
    CHECK((v.flags & sim::site_has_sun_size) == 0u);
    CHECK(v.sun_angular_diameter_rad == 0.0);
    CHECK((v.flags & sim::site_has_gravity) == 0u);
    CHECK(v.gravity_km_s2 == 0.0);
}

TEST_CASE("local solar time is noon below the Sun and midnight opposite it", "[exploration]") {
    const Simulation s = make_simulation();
    // Sites 1 m from the center toward the star and away from it (far below the synthetic surface, but on the
    // star's line through the center, so the star's direction from them is the same as from the center).
    const auto star = s.state(k_star, k_planet, FrameId::icrf(), k_t);
    REQUIRE(star.has_value());
    const Vec3 toward = usim::math::normalized(star.value().state.position_km) * 1e-3;
    const auto noon =
        sim::surface_site_below(s, k_planet, toward, FrameId::icrf(), AxisConvention::core, k_t);
    const auto midnight =
        sim::surface_site_below(s, k_planet, -toward, FrameId::icrf(), AxisConvention::core, k_t);
    REQUIRE(noon.has_value());
    REQUIRE(midnight.has_value());
    CHECK_THAT(noon.value().local_solar_time_h, WithinAbs(12.0, 1e-9));
    const double midnight_h = midnight.value().local_solar_time_h;
    CHECK_THAT(std::fmin(midnight_h, 24.0 - midnight_h), WithinAbs(0.0, 1e-9));
}

TEST_CASE("sites need a known body with a radius and a rotation model, in an inertial frame",
          "[exploration]") {
    const Simulation s = make_simulation();
    const auto at = [&](BodyId body, double lat, FrameId frame) {
        return sim::surface_site(s, body, {lat, 0.0, 0.0}, frame, AxisConvention::core, k_t);
    };
    CHECK(usim::test::failure(at(k_moon, 0.0, FrameId::icrf())) == ErrorCode::not_found); // no radius
    CHECK(usim::test::failure(at(naif(42), 0.0, FrameId::icrf())) == ErrorCode::not_found);
    CHECK(usim::test::failure(at(k_planet, 0.0, FrameId::body_fixed(k_planet))) ==
          ErrorCode::invalid_argument);
    CHECK(usim::test::failure(at(k_planet, 2.0, FrameId::icrf())) == ErrorCode::invalid_argument);
    CHECK(usim::test::failure(at(k_planet, NAN, FrameId::icrf())) == ErrorCode::invalid_argument);
    CHECK(usim::test::failure(sim::surface_site_below(s, k_planet, {0.0, 0.0, 0.0}, FrameId::icrf(),
                                                      AxisConvention::core, k_t)) ==
          ErrorCode::invalid_argument);
}

TEST_CASE("the sidereal day is the IAU elements' mean rate, else the rotation model's rate", "[exploration]") {
    const Simulation s = make_simulation();
    const auto day = sim::day_length(s, k_planet, k_t);
    REQUIRE(day.has_value());

    // The planet has IAU elements (besides its binary PCK): the secular rate W1 + 2 W2 d, deg/day.
    const auto& pm = expected::k_rotation1_pm;
    const double days = k_t_s / usim::constants::k_seconds_per_day;
    const double degrees_per_day = pm[1] + 2.0 * pm[2] * days;
    CHECK_THAT(day.value().sidereal_rotation_period_s,
               WithinRel(360.0 / degrees_per_day * usim::constants::k_seconds_per_day, 1e-14));
    CHECK((day.value().flags & sim::day_instantaneous_rate) == 0u);

    // The moon has only a binary PCK: its instantaneous rate, compared with the angle turned in a short
    // interval between two orientations. Inside one record of its synthetic orientation segment (records meet
    // at 43200 s, and synthetic records need not join smoothly).
    const double moon_t_s = 30000.0;
    const auto moon_day = sim::day_length(s, k_moon, Instant::from_seconds_since_j2000(moon_t_s));
    REQUIRE(moon_day.has_value());
    CHECK((moon_day.value().flags & sim::day_instantaneous_rate) != 0u);
    const double h = 1.0;
    const auto before = s.orientation(k_moon, FrameId::icrf(), Instant::from_seconds_since_j2000(moon_t_s - h));
    const auto after = s.orientation(k_moon, FrameId::icrf(), Instant::from_seconds_since_j2000(moon_t_s + h));
    REQUIRE(before.has_value());
    REQUIRE(after.has_value());
    const double rate = usim::math::angle_between(before.value(), after.value()) / (2.0 * h);
    CHECK_THAT(moon_day.value().sidereal_rotation_period_s, WithinRel(2.0 * std::numbers::pi / rate, 1e-6));

    // The planet orbits the star directly, so its own orbit gives the solar day.
    CHECK(day.value().orbiting_body == k_planet);
    if ((day.value().flags & sim::day_has_solar_day) != 0u) {
        const double sense = (day.value().flags & sim::day_retrograde) != 0u ? -1.0 : 1.0;
        CHECK_THAT(1.0 / day.value().mean_solar_day_s,
                   WithinRel(std::fabs(sense / day.value().sidereal_rotation_period_s -
                                       1.0 / day.value().orbital_period_s),
                             1e-12));
    }

    CHECK(usim::test::failure(sim::day_length(s, naif(1003), k_t)) ==
          ErrorCode::not_found); // no rotation model
    CHECK(usim::test::failure(sim::day_length(s, naif(42), k_t)) == ErrorCode::not_found);
}

TEST_CASE("a transfer estimate joins the two orbits around their shared center", "[exploration]") {
    const Simulation s = make_simulation();
    const auto estimate = sim::transfer_estimate(s, k_planet, k_moon, k_t);
    REQUIRE(estimate.has_value());
    const auto& e = estimate.value();
    CHECK(e.center == k_star);
    CHECK(e.departure == k_planet);
    CHECK(e.arrival == k_moon);
    CHECK((e.flags &
           (sim::transfer_from_surface | sim::transfer_to_surface | sim::transfer_between_systems)) == 0u);

    // The radii are the osculating semi-major axes of the two orbits, with the orbiting body's GM where it
    // has one.
    const double star_gm = *s.catalog().find(k_star)->gm.value;
    const double moon_gm = *s.catalog().find(k_moon)->gm.value;
    const auto planet_orbit = s.state(k_planet, k_star, FrameId::icrf(), k_t);
    const auto moon_orbit = s.state(k_moon, k_star, FrameId::icrf(), k_t);
    REQUIRE(planet_orbit.has_value());
    REQUIRE(moon_orbit.has_value());
    CHECK_THAT(
        e.departure_radius_km,
        WithinRel(usim::orbits::semi_major_axis_km(planet_orbit.value().state, star_gm).value(), 1e-15));
    CHECK_THAT(
        e.arrival_radius_km,
        WithinRel(usim::orbits::semi_major_axis_km(moon_orbit.value().state, star_gm + moon_gm).value(),
                  1e-15));
    const auto hohmann = usim::orbits::hohmann_transfer(star_gm, e.departure_radius_km, e.arrival_radius_km);
    CHECK(e.transfer.transfer_time_s == hohmann.value().transfer_time_s);
    CHECK(e.transfer.departure_delta_v_km_s == hohmann.value().departure_delta_v_km_s);

    const auto apart = s.state(k_moon, k_planet, FrameId::icrf(), k_t);
    CHECK_THAT(e.distance_km, WithinRel(usim::math::norm(apart.value().state.position_km), 1e-15));
    CHECK_THAT(e.light_time_s, WithinRel(e.distance_km / usim::constants::k_c_km_s, 1e-15));
}

TEST_CASE("transfer estimates reject what they cannot estimate", "[exploration]") {
    const Simulation s = make_simulation();
    CHECK(usim::test::failure(sim::transfer_estimate(s, k_planet, k_planet, k_t)) ==
          ErrorCode::invalid_argument);
    CHECK(usim::test::failure(sim::transfer_estimate(s, k_planet, naif(42), k_t)) == ErrorCode::not_found);
    // From the star's surface: the synthetic star has no radius.
    CHECK(usim::test::failure(sim::transfer_estimate(s, k_star, k_planet, k_t)) == ErrorCode::not_found);
    // The root has no GM: no transfer around it.
    CHECK(usim::test::failure(sim::transfer_estimate(s, naif(0), k_planet, k_t)) == ErrorCode::unsupported);
}
