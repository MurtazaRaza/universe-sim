#include "usim/constants.hpp"
#include "usim/frames/frame_id.hpp"
#include "usim/frames/frame_system.hpp"
#include "usim/frames/frame_transform.hpp"
#include "usim/math/mat3.hpp"

#include "fixtures/spice_reference.hpp"
#include "test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>

using usim::ErrorCode;
using usim::catalog::BodyId;
using usim::frames::FrameId;
using usim::frames::FrameSystem;
using usim::frames::FrameTransform;
using usim::frames::StateVector;
using usim::math::Mat3;
using usim::math::Vec3;
using usim::time::Instant;
namespace m = usim::math;
namespace spice = usim::test::spice;
namespace test = usim::test;

namespace {

Mat3 spice_icrf_to_eclipj2000() {
    const auto& r = spice::k_icrf_to_eclipj2000;
    return Mat3::from_rows({r[0][0], r[0][1], r[0][2]}, {r[1][0], r[1][1], r[1][2]}, {r[2][0], r[2][1], r[2][2]});
}

// A frame spinning about Z at `rate_rad_s`, as seen at time t: R = rotation_z(-w t) (frame rotation),
// dR/dt = -w * d(rotation_z)/d(angle) at angle -w t.
FrameTransform spinning(double rate_rad_s, double t) {
    const double a = -rate_rad_s * t;
    const Mat3 r = Mat3::rotation_z(a);
    const Mat3 dr_da = Mat3{{-std::sin(a), -std::cos(a), 0, std::cos(a), -std::sin(a), 0, 0, 0, 0}};
    return {r, dr_da * -rate_rad_s};
}

} // namespace

TEST_CASE("ICRF to ECLIPJ2000 matches CSPICE pxform", "[frames]") {
    CHECK(m::max_abs_difference(usim::frames::icrf_to_eclipj2000(), spice_icrf_to_eclipj2000()) < 2e-16);

    // The ecliptic north pole, written in ICRF coordinates, becomes +Z.
    const double eps = usim::constants::k_eclipj2000_obliquity_rad;
    const Vec3 pole_icrf{0.0, -std::sin(eps), std::cos(eps)};
    CHECK(test::max_abs_difference(usim::frames::icrf_to_eclipj2000() * pole_icrf, Vec3{0, 0, 1}) < 1e-16);
}

TEST_CASE("frame system transforms between inertial frames", "[frames]") {
    const FrameSystem frames;
    const Instant t = Instant::from_seconds_since_j2000(1.0e8);

    const auto to_ecliptic = frames.transform(FrameId::icrf(), FrameId::eclipj2000(), t);
    REQUIRE(to_ecliptic.has_value());
    CHECK(to_ecliptic.value().rotation() == usim::frames::icrf_to_eclipj2000());
    CHECK(to_ecliptic.value().rotation_rate_per_s() == Mat3{});

    const auto back = frames.transform(FrameId::eclipj2000(), FrameId::icrf(), t);
    REQUIRE(back.has_value());
    const FrameTransform round_trip = to_ecliptic.value().then(back.value());
    CHECK(m::max_abs_difference(round_trip.rotation(), Mat3::identity()) < 1e-15);

    const auto same = frames.transform(FrameId::eclipj2000(), FrameId::eclipj2000(), t);
    REQUIRE(same.has_value());
    CHECK(same.value().rotation() == Mat3::identity());

    // States: inertial frames rotate position and velocity alike.
    const StateVector s{{1.0e8, -2.0e7, 3.0e6}, {10.0, 20.0, -5.0}};
    const StateVector e = to_ecliptic.value().apply(s);
    CHECK(test::max_abs_difference(e.velocity_km_s, usim::frames::icrf_to_eclipj2000() * s.velocity_km_s) < 1e-13);
}

TEST_CASE("body-fixed frames are not available before P4-03", "[frames]") {
    const FrameSystem frames;
    const auto r = frames.transform(FrameId::icrf(), FrameId::body_fixed(BodyId::naif(399)), Instant());
    REQUIRE_FALSE(r.has_value());
    CHECK(r.error().code == ErrorCode::unsupported);
    CHECK(to_string(FrameId::body_fixed(BodyId::naif(399))) == "body-fixed(naif:399)");
    CHECK(to_string(FrameId::eclipj2000()) == "ECLIPJ2000");
    CHECK_FALSE(FrameId::body_fixed(BodyId::naif(399)).is_inertial());
    CHECK(FrameId::icrf().is_inertial());
}

TEST_CASE("rotating transforms compose and invert like 6x6 state matrices", "[frames]") {
    const double w = 1.0e-4; // synthetic rotation rate, rad/s
    const double t = 1234.5;
    const FrameTransform spin = spinning(w, t);
    const FrameTransform tilt = FrameTransform::inertial(Mat3::rotation_x(0.4));

    // Velocity term: compare with a finite difference of the transformed position of a fixed point.
    const Vec3 p{7000.0, -1200.0, 300.0};
    const double h = 1e-3;
    const Vec3 numeric = (spinning(w, t + h).apply(p) - spinning(w, t - h).apply(p)) / (2.0 * h);
    const StateVector moved = spin.apply(StateVector{p, {}});
    CHECK(test::max_abs_difference(moved.velocity_km_s, numeric) < 1e-9);

    // Composition: (tilt after spin) applied to a state equals applying them in turn.
    const StateVector s{{1.0, 2.0, 3.0}, {-0.5, 0.25, 4.0}};
    const StateVector two_steps = tilt.apply(spin.apply(s));
    const StateVector composed = spin.then(tilt).apply(s);
    CHECK(test::max_abs_difference(composed.position_km, two_steps.position_km) < 1e-14);
    CHECK(test::max_abs_difference(composed.velocity_km_s, two_steps.velocity_km_s) < 1e-14);

    // Inverse undoes position and velocity.
    const StateVector undone = spin.then(tilt).inverse().apply(composed);
    CHECK(test::max_abs_difference(undone.position_km, s.position_km) < 1e-14);
    CHECK(test::max_abs_difference(undone.velocity_km_s, s.velocity_km_s) < 1e-14);
}
