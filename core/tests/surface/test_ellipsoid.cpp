// usim::surface reference-ellipsoid geometry (ADR-0016) on a synthetic triaxial ellipsoid (not physical
// data).

#include "test_support.hpp"
#include "usim/surface/ellipsoid.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>
#include <numbers>

using Catch::Matchers::WithinAbs;
using usim::math::Vec3;
using usim::surface::Ellipsoid;
using usim::surface::PlanetocentricPoint;

namespace {

constexpr Ellipsoid k_triaxial{3.5, 2.5, 1.5};
constexpr double k_half_pi = 0.5 * std::numbers::pi;

double ellipsoid_equation(const Ellipsoid& e, const Vec3& p) {
    return (p.x / e.a_km) * (p.x / e.a_km) + (p.y / e.b_km) * (p.y / e.b_km) +
           (p.z / e.c_km) * (p.z / e.c_km);
}

} // namespace

TEST_CASE("surface points lie on the ellipsoid and round trip through planetocentric coordinates",
          "[surface]") {
    usim::test::SplitMix64 rng(0x5eed0016);
    for (int k = 0; k < 2000; ++k) {
        const PlanetocentricPoint point{rng.uniform(-1.5, 1.5), rng.uniform(-3.1, 3.1),
                                        rng.uniform(-1.0, 5.0)};
        const Vec3 p = usim::surface::position_km(k_triaxial, point);
        const PlanetocentricPoint back = usim::surface::planetocentric(k_triaxial, p);
        CHECK_THAT(back.latitude_rad, WithinAbs(point.latitude_rad, 1e-14));
        CHECK_THAT(back.longitude_rad, WithinAbs(point.longitude_rad, 1e-14));
        CHECK_THAT(back.altitude_km, WithinAbs(point.altitude_km, 1e-13));

        const Vec3 on_surface =
            usim::surface::position_km(k_triaxial, {point.latitude_rad, point.longitude_rad, 0.0});
        CHECK_THAT(ellipsoid_equation(k_triaxial, on_surface), WithinAbs(1.0, 1e-14));
    }
}

TEST_CASE("a sphere has the same radius in every direction", "[surface]") {
    constexpr Ellipsoid sphere{2.0, 2.0, 2.0};
    usim::test::SplitMix64 rng(17);
    for (int k = 0; k < 100; ++k) {
        const Vec3 d = usim::surface::direction(rng.uniform(-1.5, 1.5), rng.uniform(-3.1, 3.1));
        CHECK_THAT(usim::math::norm(d), WithinAbs(1.0, 1e-15));
        CHECK_THAT(usim::surface::radius_along(sphere, d), WithinAbs(2.0, 1e-15));
    }
}

TEST_CASE("the local frame is orthonormal and right-handed, with up normal to the surface", "[surface]") {
    usim::test::SplitMix64 rng(0x0b0d1e5);
    const double h = 1e-6;
    for (int k = 0; k < 500; ++k) {
        const double lat = rng.uniform(-1.5, 1.5);
        const double lon = rng.uniform(-3.1, 3.1);
        const auto frame = usim::surface::local_frame(k_triaxial, lat, lon);
        CHECK_THAT(usim::math::norm(frame.up), WithinAbs(1.0, 1e-15));
        CHECK_THAT(usim::math::norm(frame.north), WithinAbs(1.0, 1e-15));
        CHECK_THAT(usim::math::norm(frame.east), WithinAbs(1.0, 1e-15));
        CHECK_THAT(usim::math::dot(frame.up, frame.north), WithinAbs(0.0, 1e-15));
        CHECK_THAT(usim::math::dot(frame.up, frame.east), WithinAbs(0.0, 1e-15));
        CHECK_THAT(usim::math::dot(frame.north, frame.east), WithinAbs(0.0, 1e-15));
        // East, north, up in that order are right-handed, like x, y, z.
        CHECK(usim::test::max_abs_difference(usim::math::cross(frame.east, frame.north), frame.up) < 1e-15);

        // Moving along the surface stays in the horizon plane, north with latitude and east with longitude.
        const auto at = [&](double la, double lo) {
            return usim::surface::position_km(k_triaxial, {la, lo, 0.0});
        };
        const Vec3 along_meridian = (at(lat + h, lon) - at(lat - h, lon)) / (2.0 * h);
        const Vec3 along_parallel = (at(lat, lon + h) - at(lat, lon - h)) / (2.0 * h);
        CHECK(std::fabs(usim::math::dot(along_meridian, frame.up)) < 1e-8 * usim::math::norm(along_meridian));
        CHECK(std::fabs(usim::math::dot(along_parallel, frame.up)) < 1e-8 * usim::math::norm(along_parallel));
        CHECK(usim::math::dot(along_meridian, frame.north) > 0.0);
        CHECK(usim::math::dot(along_parallel, frame.east) > 0.0);
    }
}

TEST_CASE("the local frame is continuous over the poles", "[surface]") {
    for (const double lon : {-2.5, 0.0, 0.7, 3.0}) {
        for (const double pole : {k_half_pi, -k_half_pi}) {
            const auto at_pole = usim::surface::local_frame(k_triaxial, pole, lon);
            const auto near = usim::surface::local_frame(k_triaxial, pole - std::copysign(1e-9, pole), lon);
            CHECK(usim::test::max_abs_difference(at_pole.up, near.up) < 1e-8);
            CHECK(usim::test::max_abs_difference(at_pole.north, near.north) < 1e-8);
            CHECK(usim::test::max_abs_difference(at_pole.east, near.east) < 1e-8);
        }
    }
}
