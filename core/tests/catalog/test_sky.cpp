// The star direction model (SPEC FR-REN-09) on made-up stars (not catalog data).

#include "usim/catalog/sky.hpp"

#include <catch2/catch_approx.hpp>
#include <catch2/catch_test_macros.hpp>

#include <cmath>

using usim::catalog::ProperMotion;
using usim::catalog::Star;
using usim::catalog::star_direction_icrf;
using usim::math::Vec3;

namespace {

Star star(double ra, double dec, std::optional<ProperMotion> motion = std::nullopt) {
    Star s;
    s.ra_rad = ra;
    s.dec_rad = dec;
    s.proper_motion = motion;
    return s;
}

} // namespace

TEST_CASE("a star without proper motion points at its catalog position", "[catalog][sky]") {
    const Vec3 d = star_direction_icrf(star(0.5, -0.25), 0.0, 1.0e9);
    CHECK(d.x == Catch::Approx(std::cos(-0.25) * std::cos(0.5)).epsilon(1e-15));
    CHECK(d.y == Catch::Approx(std::cos(-0.25) * std::sin(0.5)).epsilon(1e-15));
    CHECK(d.z == Catch::Approx(std::sin(-0.25)).epsilon(1e-15));
    CHECK(usim::math::norm(star_direction_icrf(star(0.0, 1.5707963267948966), 0.0, 0.0)) == Catch::Approx(1.0));
}

TEST_CASE("proper motion moves a star along its tangent plane", "[catalog][sky]") {
    // 1e-12 rad/s east and 2e-12 rad/s north for 1e6 s: small angles, so the motion is nearly linear.
    const Star s = star(1.0, 0.25, ProperMotion{1.0e-12, 2.0e-12});
    const double epoch = 500.0;
    const Vec3 at_epoch = star_direction_icrf(s, epoch, epoch);
    const Vec3 later = star_direction_icrf(s, epoch, epoch + 1.0e6);
    CHECK(usim::math::norm(later) == Catch::Approx(1.0).epsilon(1e-15));
    const Vec3 east{-std::sin(1.0), std::cos(1.0), 0.0};
    const Vec3 north{-std::sin(0.25) * std::cos(1.0), -std::sin(0.25) * std::sin(1.0), std::cos(0.25)};
    const Vec3 moved = later - at_epoch;
    CHECK(usim::math::dot(moved, east) == Catch::Approx(1.0e-6).epsilon(1e-6));
    CHECK(usim::math::dot(moved, north) == Catch::Approx(2.0e-6).epsilon(1e-6));
    // Backward in time it moves the other way.
    CHECK(usim::math::dot(star_direction_icrf(s, epoch, epoch - 1.0e6) - at_epoch, east) < 0.0);
}
