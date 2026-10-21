#include "usim/math/angle.hpp"
#include "usim/math/mat3.hpp"
#include "usim/math/vec3.hpp"

#include "test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>
#include <limits>

using Catch::Matchers::WithinAbs;
using usim::math::Mat3;
using usim::math::Vec3;
namespace m = usim::math;
namespace test = usim::test;

TEST_CASE("Vec3 arithmetic", "[math][vec3]") {
    const Vec3 a{1, 2, 3};
    const Vec3 b{4, -5, 6};
    CHECK(a + b == Vec3{5, -3, 9});
    CHECK(a - b == Vec3{-3, 7, -3});
    CHECK(-a == Vec3{-1, -2, -3});
    CHECK(a * 2.0 == Vec3{2, 4, 6});
    CHECK(2.0 * a == a * 2.0);
    CHECK(a / 2.0 == Vec3{0.5, 1, 1.5});
    CHECK(m::dot(a, b) == 12.0);

    Vec3 c = a;
    c += b;
    c -= a;
    CHECK(c == b);
}

TEST_CASE("Vec3 cross product is right-handed", "[math][vec3]") {
    CHECK(m::cross(Vec3{1, 0, 0}, Vec3{0, 1, 0}) == Vec3{0, 0, 1});
    CHECK(m::cross(Vec3{0, 1, 0}, Vec3{0, 0, 1}) == Vec3{1, 0, 0});
    const Vec3 a{1, 2, 3};
    const Vec3 b{-2, 0.5, 4};
    CHECK(m::dot(m::cross(a, b), a) == 0.0);
}

TEST_CASE("Vec3 length and normalization", "[math][vec3]") {
    CHECK(m::norm(Vec3{3, 4, 12}) == 13.0);
    CHECK(m::norm_squared(Vec3{3, 4, 12}) == 169.0);
    CHECK(m::distance(Vec3{1, 1, 1}, Vec3{4, 5, 13}) == 13.0);
    CHECK_THAT(m::norm(m::normalized(Vec3{1e300, 1e300, 0})), WithinAbs(1.0, 1e-15));
    CHECK(m::normalized(Vec3{}) == Vec3{});
    // hypot avoids overflow where the naive sqrt(x^2 + y^2 + z^2) would give infinity.
    CHECK(std::isfinite(m::norm(Vec3{1e200, 1e200, 1e200})));
}

TEST_CASE("Mat3 active rotations follow the right-hand rule", "[math][mat3]") {
    const double quarter = m::k_half_pi;
    CHECK(test::max_abs_difference(Mat3::rotation_x(quarter) * Vec3{0, 1, 0}, Vec3{0, 0, 1}) < 1e-16);
    CHECK(test::max_abs_difference(Mat3::rotation_y(quarter) * Vec3{0, 0, 1}, Vec3{1, 0, 0}) < 1e-16);
    CHECK(test::max_abs_difference(Mat3::rotation_z(quarter) * Vec3{1, 0, 0}, Vec3{0, 1, 0}) < 1e-16);
}

TEST_CASE("Mat3 products, transpose and determinant", "[math][mat3]") {
    const Mat3 r = Mat3::rotation_z(0.3) * Mat3::rotation_x(-1.1) * Mat3::rotation_y(2.0);
    CHECK_THAT(m::determinant(r), WithinAbs(1.0, 1e-15));
    CHECK(m::max_abs_difference(m::transpose(r) * r, Mat3::identity()) < 1e-15);
    CHECK(m::transpose(m::transpose(r)) == r);

    const Mat3 a = Mat3::from_rows({1, 2, 3}, {4, 5, 6}, {7, 8, 10});
    CHECK(a(1, 2) == 6.0);
    CHECK(a.row(2) == Vec3{7, 8, 10});
    CHECK(a.column(0) == Vec3{1, 4, 7});
    CHECK(Mat3::from_columns({1, 4, 7}, {2, 5, 8}, {3, 6, 10}) == a);
    CHECK(m::determinant(a) == -3.0);
    CHECK(a * Vec3{1, 0, -1} == Vec3{-2, -2, -3});
    CHECK((a + a) == a * 2.0);

    // Applying a * b equals applying b, then a.
    const Mat3 b = Mat3::rotation_x(0.7);
    const Vec3 v{0.3, -1.2, 2.5};
    CHECK(test::max_abs_difference((a * b) * v, a * (b * v)) < 1e-14);
}

TEST_CASE("angle helpers", "[math][angle]") {
    CHECK(m::deg_to_rad(180.0) == m::k_pi);
    CHECK(m::rad_to_deg(m::k_half_pi) == 90.0);
    CHECK_THAT(m::arcsec_to_rad(648'000.0), WithinAbs(m::k_pi, 1e-15));

    CHECK(m::wrap_two_pi(0.0) == 0.0);
    CHECK(m::wrap_two_pi(m::k_two_pi) == 0.0);
    CHECK_THAT(m::wrap_two_pi(-0.1), WithinAbs(m::k_two_pi - 0.1, 1e-15));
    CHECK_THAT(m::wrap_two_pi(7.0 * m::k_pi), WithinAbs(m::k_pi, 1e-14));
    CHECK(m::wrap_two_pi(-1e-300) < m::k_two_pi);
    CHECK(std::isnan(m::wrap_two_pi(std::numeric_limits<double>::infinity())));

    CHECK(m::wrap_pi(m::k_pi) == -m::k_pi);
    CHECK_THAT(m::wrap_pi(3.0 * m::k_half_pi), WithinAbs(-m::k_half_pi, 1e-15));
    CHECK_THAT(m::wrap_pi(-0.25), WithinAbs(-0.25, 1e-16));
}
