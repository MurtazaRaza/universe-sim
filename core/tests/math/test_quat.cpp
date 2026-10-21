#include "usim/math/angle.hpp"
#include "usim/math/mat3.hpp"
#include "usim/math/quat.hpp"

#include "fixtures/spice_reference.hpp"
#include "test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>

using Catch::Matchers::WithinAbs;
using usim::math::Mat3;
using usim::math::Quat;
using usim::math::Vec3;
namespace m = usim::math;
namespace spice = usim::test::spice;
namespace test = usim::test;

namespace {

Mat3 to_mat3(const double (&rows)[3][3]) {
    return Mat3::from_rows({rows[0][0], rows[0][1], rows[0][2]}, {rows[1][0], rows[1][1], rows[1][2]},
                           {rows[2][0], rows[2][1], rows[2][2]});
}

double max_component_difference(const Quat& a, const Quat& b) {
    return std::fmax(std::fmax(std::fabs(a.w - b.w), std::fabs(a.x - b.x)),
                     std::fmax(std::fabs(a.y - b.y), std::fabs(a.z - b.z)));
}

} // namespace

TEST_CASE("quaternions follow the SPICE convention (axisar, m2q, q2m)", "[math][quat]") {
    for (const auto& c : spice::k_axis_angle_cases) {
        const Vec3 axis{c.axis[0], c.axis[1], c.axis[2]};
        const Mat3 expected = to_mat3(c.matrix);
        const Quat expected_q{c.quat_wxyz[0], c.quat_wxyz[1], c.quat_wxyz[2], c.quat_wxyz[3]};

        const Quat q = Quat::from_axis_angle(axis, c.angle_rad);
        CHECK(m::max_abs_difference(m::to_matrix(q), expected) < 1e-15);
        CHECK(max_component_difference(q, expected_q) < 1e-15);

        // m2q returns w >= 0, and so does from_matrix.
        CHECK(max_component_difference(Quat::from_matrix(expected), expected_q) < 1e-15);
    }
}

TEST_CASE("Hamilton product and composition", "[math][quat]") {
    const Quat i{0, 1, 0, 0};
    const Quat j{0, 0, 1, 0};
    const Quat k{0, 0, 0, 1};
    CHECK(i * j == k);
    CHECK(j * k == i);
    CHECK(k * i == j);
    CHECK(i * i == Quat{-1, 0, 0, 0});

    test::SplitMix64 rng(1);
    for (int n = 0; n < 200; ++n) {
        const Quat a = test::random_rotation(rng);
        const Quat b = test::random_rotation(rng);
        const Vec3 v = test::random_vector(rng, 10.0);
        // As rotations, a * b applies b first.
        CHECK(m::max_abs_difference(m::to_matrix(a * b), m::to_matrix(a) * m::to_matrix(b)) < 1e-14);
        CHECK(test::max_abs_difference(m::rotate(a, v), m::to_matrix(a) * v) < 1e-13);
        CHECK(test::max_abs_difference(m::rotate(m::conjugate(a), m::rotate(a, v)), v) < 1e-13);
    }
}

TEST_CASE("matrix round trip covers every Shepperd branch", "[math][quat]") {
    // Rotations by nearly pi about each axis make x, y or z the largest component.
    const Quat cases[] = {
        Quat::from_axis_angle({1, 0.1, -0.2}, 3.1),  Quat::from_axis_angle({0.1, 1, 0.2}, 3.1),
        Quat::from_axis_angle({-0.2, 0.1, 1}, 3.1),  Quat::from_axis_angle({1, 1, 1}, 0.2),
        Quat::from_axis_angle({0, 0, 1}, m::k_pi),   Quat::identity(),
    };
    for (const Quat& q : cases) {
        const Quat back = Quat::from_matrix(m::to_matrix(q));
        CHECK(back.w >= 0.0);
        CHECK(m::same_rotation(back, q, 1e-14));
        CHECK_THAT(m::norm(back), WithinAbs(1.0, 1e-15));
    }

    test::SplitMix64 rng(2);
    for (int n = 0; n < 1000; ++n) {
        const Quat q = test::random_rotation(rng);
        CHECK(m::same_rotation(Quat::from_matrix(m::to_matrix(q)), q, 1e-14));
    }
}

TEST_CASE("q and -q are the same rotation", "[math][quat]") {
    test::SplitMix64 rng(3);
    for (int n = 0; n < 100; ++n) {
        const Quat q = test::random_rotation(rng);
        CHECK(m::max_abs_difference(m::to_matrix(q), m::to_matrix(-q)) == 0.0);
        CHECK(m::angle_between(q, -q) < 1e-15);
        CHECK(m::same_rotation(q, -q, 1e-15));
        // from_matrix picks the representative with w >= 0, whichever sign went in.
        const Quat canonical = Quat::from_matrix(m::to_matrix(q.w < 0.0 ? q : -q));
        CHECK(canonical.w >= 0.0);
    }
}

TEST_CASE("slerp interpolates along the shorter arc", "[math][quat]") {
    const Quat a = Quat::from_axis_angle({0, 0, 1}, 0.2);
    const Quat b = Quat::from_axis_angle({0, 0, 1}, 1.4);

    CHECK(m::same_rotation(m::slerp(a, b, 0.0), a, 1e-15));
    CHECK(m::same_rotation(m::slerp(a, b, 1.0), b, 1e-15));
    CHECK(m::same_rotation(m::slerp(a, b, 0.25), Quat::from_axis_angle({0, 0, 1}, 0.5), 1e-15));

    // Passing -b must not take the long way around.
    CHECK(m::same_rotation(m::slerp(a, -b, 0.5), Quat::from_axis_angle({0, 0, 1}, 0.8), 1e-15));

    // Nearly identical rotations use the linear fallback and stay normalized.
    const Quat c = Quat::from_axis_angle({1, 2, 3}, 1e-9);
    CHECK_THAT(m::norm(m::slerp(Quat::identity(), c, 0.5)), WithinAbs(1.0, 1e-15));
    CHECK(m::same_rotation(m::slerp(Quat::identity(), c, 0.5), Quat::from_axis_angle({1, 2, 3}, 5e-10), 1e-15));
}

TEST_CASE("degenerate inputs", "[math][quat]") {
    CHECK(Quat::from_axis_angle({0, 0, 0}, 1.0) == Quat::identity());
    CHECK(m::normalized(Quat{0, 0, 0, 0}) == Quat::identity());
    CHECK_THAT(m::angle_between(Quat::identity(), Quat::from_axis_angle({0, 1, 0}, 2.5)), WithinAbs(2.5, 1e-15));
}
