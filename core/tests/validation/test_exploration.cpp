// ADR-0016 on the default pack: day lengths, transfer estimates and surface sites against independent
// computations from the same pack's states, orientations, GM and radii. No expected value is typed in; each
// check derives it another way.

#include "usim/constants.hpp"
#include "usim/math/quat.hpp"
#include "usim/sim/exploration.hpp"
#include "validation/validation_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>
#include <cstdio>
#include <numbers>

namespace v = usim::test::validation;
using Catch::Matchers::WithinAbs;
using Catch::Matchers::WithinRel;
using usim::catalog::BodyId;
using usim::frames::FrameId;
using usim::math::Vec3;
using usim::presentation::AxisConvention;
using usim::sim::Simulation;
using usim::time::Instant;
namespace sim = usim::sim;

namespace {

constexpr double k_two_pi = 2.0 * std::numbers::pi;
const Instant k_j2000 = Instant::from_seconds_since_j2000(0.0);

const Simulation& default_simulation() {
    static const Simulation s = [] {
        auto created = Simulation::create(v::default_pack());
        if (!created) {
            FAIL("cannot create the simulation: " << created.error().message);
        }
        return std::move(created).value();
    }();
    return s;
}

BodyId naif(std::int64_t id) {
    return BodyId::naif(id);
}

// The longitude of the Sun in the body's body-fixed frame at t, from the ephemeris and the orientation
// directly.
double sun_longitude(const Simulation& s, BodyId body, Instant t) {
    const auto sun = s.state(naif(10), body, FrameId::icrf(), t);
    const auto q = s.orientation(body, FrameId::icrf(), t);
    REQUIRE(sun.has_value());
    REQUIRE(q.has_value());
    const Vec3 fixed = usim::math::rotate(usim::math::conjugate(q.value()), sun.value().state.position_km);
    return std::atan2(fixed.y, fixed.x);
}

// Vis-viva, written out here independently of usim::orbits.
double semi_major_axis(const usim::frames::StateVector& state, double gm) {
    const double r = usim::math::norm(state.position_km);
    const double v2 = usim::math::norm_squared(state.velocity_km_s);
    return 1.0 / (2.0 / r - v2 / gm);
}

double gm_of(const Simulation& s, std::int64_t id) {
    return *s.catalog().find(naif(id))->gm.value;
}

} // namespace

TEST_CASE("the mean solar day matches the Sun's hour angle followed over whole orbits",
          "[validation][exploration]") {
    const Simulation& s = default_simulation();
    // Mercury, Venus (retrograde), the Earth, the Moon (synchronous, binary PCK) and Mars.
    for (const std::int64_t id : {199, 299, 399, 301, 499}) {
        const BodyId body = naif(id);
        const auto day = sim::day_length(s, body, k_j2000);
        REQUIRE(day.has_value());
        REQUIRE((day.value().flags & sim::day_has_solar_day) != 0u);
        const double solar = day.value().mean_solar_day_s;
        const double orbit = day.value().orbital_period_s;

        // Over whole orbits the equation of time comes back to where it started, so the mean rate of the
        // Sun's longitude in the body's frame gives the mean solar day. At least 20 solar days and two
        // orbits.
        const double orbits = std::fmax(2.0, std::ceil(20.0 * solar / orbit));
        const double span = orbits * orbit;
        const double step = solar / 96.0;
        const auto steps = static_cast<long>(std::ceil(span / step));
        double previous = sun_longitude(s, body, k_j2000);
        double turned = 0.0;
        for (long k = 1; k <= steps; ++k) {
            const double t = std::fmin(static_cast<double>(k) * step, span);
            const double now = sun_longitude(s, body, Instant::from_seconds_since_j2000(t));
            turned += std::remainder(now - previous, k_two_pi);
            previous = now;
        }
        const double measured = k_two_pi * span / std::fabs(turned);
        const double difference = std::fabs(measured - solar) / solar;
        std::printf(
            "VAL exploration: naif %lld mean solar day %.6f s, from the hour angle %.6f s over %.0f orbits, "
            "relative difference %.2e\n",
            static_cast<long long>(id), solar, measured, orbits, difference);
        // Measured 2026-11-02 (MSVC 19.50): Mercury 1.2e-6, Venus 4.7e-6, the Earth 5.2e-8, the Moon 1.1e-5,
        // Mars 6.4e-10. The residue is the osculating orbital period at one instant against the mean over the
        // span. With the instantaneous rotation rate instead of the IAU mean rate, Mercury's and the Moon's
        // librations gave 4.4e-4 and 9.6e-5 (ADR-0016).
        CHECK(difference < 5e-5);
    }
}

TEST_CASE("transfer estimates follow from the pack's states, GM and radii", "[validation][exploration]") {
    const Simulation& s = default_simulation();
    const double sun_gm = gm_of(s, 10);

    SECTION("the Earth to Mars around the Sun, at the system barycenters") {
        const auto there = sim::transfer_estimate(s, naif(399), naif(499), k_j2000);
        REQUIRE(there.has_value());
        const auto& e = there.value();
        CHECK(e.center == naif(10));
        CHECK(e.departure == naif(399));
        CHECK(e.arrival == naif(499));
        CHECK((e.flags & (sim::transfer_from_surface | sim::transfer_to_surface |
                          sim::transfer_between_systems)) == 0u);
        const auto earth = s.state(naif(3), naif(10), FrameId::icrf(), k_j2000);
        const auto mars = s.state(naif(4), naif(10), FrameId::icrf(), k_j2000);
        CHECK_THAT(e.departure_radius_km,
                   WithinRel(semi_major_axis(earth.value().state, sun_gm + gm_of(s, 3)), 1e-14));
        CHECK_THAT(e.arrival_radius_km,
                   WithinRel(semi_major_axis(mars.value().state, sun_gm + gm_of(s, 4)), 1e-14));
        const double a = 0.5 * (e.departure_radius_km + e.arrival_radius_km);
        CHECK_THAT(e.transfer.transfer_time_s,
                   WithinRel(std::numbers::pi * std::sqrt(a * a * a / sun_gm), 1e-14));

        const auto apart = s.state(naif(499), naif(399), FrameId::icrf(), k_j2000);
        CHECK_THAT(e.distance_km, WithinRel(usim::math::norm(apart.value().state.position_km), 1e-15));
        CHECK_THAT(e.light_time_s, WithinRel(e.distance_km / usim::constants::k_c_km_s, 1e-15));
        CHECK(((e.flags & sim::transfer_approximate) != 0u) == apart.value().quality.approximate);

        // The way back takes as long, with the speed changes swapped.
        const auto back = sim::transfer_estimate(s, naif(499), naif(399), k_j2000);
        REQUIRE(back.has_value());
        CHECK_THAT(back.value().transfer.transfer_time_s, WithinRel(e.transfer.transfer_time_s, 1e-15));
        CHECK_THAT(back.value().transfer.departure_delta_v_km_s,
                   WithinRel(e.transfer.arrival_delta_v_km_s, 1e-14));
    }

    SECTION("the Earth to the Moon starts at the Earth's surface") {
        const auto e = sim::transfer_estimate(s, naif(399), naif(301), k_j2000);
        REQUIRE(e.has_value());
        CHECK(e.value().center == naif(399));
        CHECK(e.value().departure == naif(399));
        CHECK(e.value().arrival == naif(301));
        CHECK(e.value().flags == sim::transfer_from_surface);
        CHECK(e.value().departure_radius_km == *s.catalog().find(naif(399))->mean_radius.value);
        const auto moon = s.state(naif(301), naif(399), FrameId::icrf(), k_j2000);
        CHECK_THAT(e.value().arrival_radius_km,
                   WithinRel(semi_major_axis(moon.value().state, gm_of(s, 399) + gm_of(s, 301)), 1e-14));
    }

    SECTION("the Moon to Mars goes between the Earth's and Mars's orbits") {
        const auto e = sim::transfer_estimate(s, naif(301), naif(499), k_j2000);
        REQUIRE(e.has_value());
        CHECK(e.value().center == naif(10));
        CHECK(e.value().departure == naif(399));
        CHECK(e.value().arrival == naif(499));
        CHECK((e.value().flags & sim::transfer_between_systems) != 0u);
    }

    SECTION("to the Sun ends at its surface") {
        const auto e = sim::transfer_estimate(s, naif(399), naif(10), k_j2000);
        REQUIRE(e.has_value());
        CHECK(e.value().center == naif(10));
        CHECK((e.value().flags & sim::transfer_to_surface) != 0u);
        CHECK(e.value().arrival_radius_km == *s.catalog().find(naif(10))->mean_radius.value);
    }
}

TEST_CASE("the Sun is overhead at the subsolar point and underfoot opposite it",
          "[validation][exploration]") {
    const Simulation& s = default_simulation();
    for (const std::int64_t id : {399, 301, 499}) {
        const BodyId body = naif(id);
        // The Sun's direction in the body-fixed frame, from the ephemeris and the orientation directly.
        const auto sun = s.state(naif(10), body, FrameId::icrf(), k_j2000);
        const auto q = s.orientation(body, FrameId::icrf(), k_j2000);
        const Vec3 fixed =
            usim::math::rotate(usim::math::conjugate(q.value()), sun.value().state.position_km);
        const double lat = std::atan2(fixed.z, std::hypot(fixed.x, fixed.y));
        const double lon = std::atan2(fixed.y, fixed.x);

        const auto noon = sim::surface_site(s, body, {lat, lon, 0.0}, FrameId::eclipj2000(),
                                            AxisConvention::unity, k_j2000);
        REQUIRE(noon.has_value());
        const auto& n = noon.value();
        // The site lies on the line from the center to the Sun, so the Sun is straight up along the radius;
        // the horizon is tilted from the radius by the ellipsoid's flattening.
        const double tilt =
            std::acos(std::fmin(1.0, usim::math::dot(usim::math::normalized(n.position_km), n.up)));
        CHECK_THAT(n.sun_elevation_rad, WithinAbs(0.5 * std::numbers::pi - tilt, 1e-9));
        CHECK_THAT(n.local_solar_time_h, WithinAbs(12.0, 1e-9));
        const double d = usim::math::norm(sun.value().state.position_km) - usim::math::norm(n.position_km);
        CHECK_THAT(n.sun_distance_km, WithinRel(d, 1e-12));
        const double ratio = usim::constants::k_au_km / d;
        CHECK_THAT(n.sunlight_ratio, WithinRel(ratio * ratio, 1e-12));
        const double sun_radius = *s.catalog().find(naif(10))->mean_radius.value;
        CHECK_THAT(n.sun_angular_diameter_rad, WithinRel(2.0 * std::asin(sun_radius / d), 1e-12));
        const double gm = *s.catalog().find(body)->gm.value;
        CHECK_THAT(n.gravity_km_s2, WithinRel(gm / usim::math::norm_squared(n.position_km), 1e-14));
        CHECK_THAT(n.escape_speed_km_s,
                   WithinRel(std::sqrt(2.0 * gm / usim::math::norm(n.position_km)), 1e-14));

        const auto midnight = sim::surface_site(s, body, {-lat, lon + std::numbers::pi, 0.0},
                                                FrameId::eclipj2000(), AxisConvention::unity, k_j2000);
        REQUIRE(midnight.has_value());
        CHECK(midnight.value().sun_elevation_rad < -0.5 * std::numbers::pi + 0.01);
        const double h = midnight.value().local_solar_time_h;
        CHECK_THAT(std::fmin(h, 24.0 - h), WithinAbs(0.0, 1e-9));
    }
}
