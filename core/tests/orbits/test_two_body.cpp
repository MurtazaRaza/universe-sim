// usim::orbits two-body helpers (ADR-0016) on synthetic values (not physical data). Each result is checked
// against an independent relation (vis-viva at the other apse, circumference over speed) rather than the
// formula it implements.

#include "test_support.hpp"
#include "usim/orbits/two_body.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>
#include <numbers>

using Catch::Matchers::WithinAbs;
using Catch::Matchers::WithinRel;
using usim::ErrorCode;
using usim::frames::StateVector;
using usim::orbits::hohmann_transfer;
using usim::orbits::orbital_period_s;
using usim::orbits::semi_major_axis_km;

namespace {

// Speed on an orbit of semi-major axis a at distance r (vis-viva).
double vis_viva_speed(double gm, double r, double a) {
    return std::sqrt(gm * (2.0 / r - 1.0 / a));
}

} // namespace

TEST_CASE("the semi-major axis of a circular orbit is its radius", "[orbits]") {
    const double gm = 3.0;
    const double r = 2.0;
    const StateVector circular{{0.0, r, 0.0}, {-std::sqrt(gm / r), 0.0, 0.0}};
    const auto a = semi_major_axis_km(circular, gm);
    REQUIRE(a.has_value());
    CHECK_THAT(a.value(), WithinRel(r, 1e-15));
}

TEST_CASE("an ellipse has the same semi-major axis at both apses", "[orbits]") {
    const double gm = 5.0;
    const double periapsis = 1.5;
    const double apoapsis = 4.5;
    const double expected = 0.5 * (periapsis + apoapsis);
    const StateVector at_periapsis{{periapsis, 0.0, 0.0},
                                   {0.0, vis_viva_speed(gm, periapsis, expected), 0.0}};
    const StateVector at_apoapsis{{0.0, 0.0, -apoapsis}, {vis_viva_speed(gm, apoapsis, expected), 0.0, 0.0}};
    CHECK_THAT(semi_major_axis_km(at_periapsis, gm).value(), WithinRel(expected, 1e-14));
    CHECK_THAT(semi_major_axis_km(at_apoapsis, gm).value(), WithinRel(expected, 1e-14));
}

TEST_CASE("unbound orbits and invalid inputs are rejected", "[orbits]") {
    const double gm = 2.0;
    const double r = 4.0;
    const double escape = std::sqrt(2.0 * gm / r);
    // Just above the escape speed (exactly at it, rounding decides).
    CHECK(usim::test::failure(semi_major_axis_km({{r, 0.0, 0.0}, {0.0, escape * (1.0 + 1e-12), 0.0}}, gm)) ==
          ErrorCode::unsupported);
    CHECK(usim::test::failure(semi_major_axis_km({{r, 0.0, 0.0}, {0.0, 2.0 * escape, 0.0}}, gm)) ==
          ErrorCode::unsupported);
    CHECK(usim::test::failure(semi_major_axis_km({{r, 0.0, 0.0}, {0.0, 0.1, 0.0}}, 0.0)) ==
          ErrorCode::invalid_argument);
    CHECK(usim::test::failure(semi_major_axis_km({{r, 0.0, 0.0}, {0.0, 0.1, 0.0}}, -gm)) ==
          ErrorCode::invalid_argument);
    CHECK(usim::test::failure(semi_major_axis_km({{0.0, 0.0, 0.0}, {0.0, 0.1, 0.0}}, gm)) ==
          ErrorCode::invalid_argument);
    CHECK(usim::test::failure(semi_major_axis_km({{r, 0.0, NAN}, {0.0, 0.1, 0.0}}, gm)) ==
          ErrorCode::invalid_argument);
}

TEST_CASE("a circular orbit's period is its circumference over its speed", "[orbits]") {
    const double gm = 2.0;
    const double r = 4.0;
    CHECK_THAT(orbital_period_s(r, gm), WithinRel(2.0 * std::numbers::pi * r / std::sqrt(gm / r), 1e-15));
}

TEST_CASE("a Hohmann transfer reaches the other orbit", "[orbits]") {
    const double gm = 7.0;
    const double r1 = 2.0;
    const double r2 = 9.0;
    const auto outward = hohmann_transfer(gm, r1, r2);
    REQUIRE(outward.has_value());

    // Speeding up by the departure change on the circular orbit at r1 gives an ellipse whose apoapsis is r2.
    const double departure_speed = std::sqrt(gm / r1) + outward.value().departure_delta_v_km_s;
    const auto a = semi_major_axis_km({{r1, 0.0, 0.0}, {0.0, departure_speed, 0.0}}, gm);
    REQUIRE(a.has_value());
    CHECK_THAT(2.0 * a.value() - r1, WithinRel(r2, 1e-14));
    // At apoapsis the arrival change brings it up to the circular speed at r2.
    CHECK_THAT(vis_viva_speed(gm, r2, a.value()) + outward.value().arrival_delta_v_km_s,
               WithinRel(std::sqrt(gm / r2), 1e-14));
    // The trip is half of the ellipse.
    CHECK_THAT(outward.value().transfer_time_s, WithinRel(0.5 * orbital_period_s(a.value(), gm), 1e-14));

    // Inward takes as long, with the two speed changes swapped.
    const auto inward = hohmann_transfer(gm, r2, r1);
    REQUIRE(inward.has_value());
    CHECK_THAT(inward.value().transfer_time_s, WithinRel(outward.value().transfer_time_s, 1e-15));
    CHECK_THAT(inward.value().departure_delta_v_km_s, WithinRel(outward.value().arrival_delta_v_km_s, 1e-14));
    CHECK_THAT(inward.value().arrival_delta_v_km_s, WithinRel(outward.value().departure_delta_v_km_s, 1e-14));
}

TEST_CASE("a Hohmann transfer between equal orbits costs nothing and takes half a revolution", "[orbits]") {
    const double gm = 3.0;
    const double r = 5.0;
    const auto same = hohmann_transfer(gm, r, r);
    REQUIRE(same.has_value());
    CHECK_THAT(same.value().departure_delta_v_km_s, WithinAbs(0.0, 1e-15));
    CHECK_THAT(same.value().arrival_delta_v_km_s, WithinAbs(0.0, 1e-15));
    CHECK_THAT(same.value().transfer_time_s, WithinRel(0.5 * orbital_period_s(r, gm), 1e-15));
}

TEST_CASE("a Hohmann transfer needs positive finite inputs", "[orbits]") {
    CHECK(usim::test::failure(hohmann_transfer(0.0, 1.0, 2.0)) == ErrorCode::invalid_argument);
    CHECK(usim::test::failure(hohmann_transfer(1.0, 0.0, 2.0)) == ErrorCode::invalid_argument);
    CHECK(usim::test::failure(hohmann_transfer(1.0, 1.0, -2.0)) == ErrorCode::invalid_argument);
    CHECK(usim::test::failure(hohmann_transfer(1.0, 1.0, INFINITY)) == ErrorCode::invalid_argument);
}
