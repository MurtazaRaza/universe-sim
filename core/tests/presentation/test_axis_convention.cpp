#include "usim/math/mat3.hpp"
#include "usim/math/quat.hpp"
#include "usim/presentation/axis_convention.hpp"

#include "test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <array>

using Catch::Matchers::WithinAbs;
using usim::math::Mat3;
using usim::math::Quat;
using usim::math::Vec3;
using usim::presentation::AxisConvention;
namespace m = usim::math;
namespace p = usim::presentation;
namespace test = usim::test;

namespace {

constexpr std::array<AxisConvention, 4> k_all{AxisConvention::core, AxisConvention::unity, AxisConvention::godot,
                                              AxisConvention::unreal};

} // namespace

TEST_CASE("axis matrices are signed permutations with the right handedness", "[presentation][axes]") {
    for (const AxisConvention c : k_all) {
        INFO(p::to_string(c));
        const Mat3 s = p::axis_matrix(c);
        CHECK(m::max_abs_difference(m::transpose(s) * s, Mat3::identity()) == 0.0);
        CHECK(m::determinant(s) == (p::is_left_handed(c) ? -1.0 : 1.0));
    }
}

TEST_CASE("vector mappings match ARCHITECTURE section 5.4", "[presentation][axes]") {
    const Vec3 v{1, 2, 3};
    CHECK(p::to_host(AxisConvention::core, v) == Vec3{1, 2, 3});
    CHECK(p::to_host(AxisConvention::unity, v) == Vec3{1, 3, 2});
    CHECK(p::to_host(AxisConvention::godot, v) == Vec3{1, 3, -2});
    CHECK(p::to_host(AxisConvention::unreal, v) == Vec3{1, -2, 3});

    // The ecliptic north pole (core +Z) is "up" in every host.
    CHECK(p::to_host(AxisConvention::unity, Vec3{0, 0, 1}) == Vec3{0, 1, 0});
    CHECK(p::to_host(AxisConvention::godot, Vec3{0, 0, 1}) == Vec3{0, 1, 0});
    CHECK(p::to_host(AxisConvention::unreal, Vec3{0, 0, 1}) == Vec3{0, 0, 1});
}

TEST_CASE("quaternion mappings match ARCHITECTURE section 5.4", "[presentation][axes]") {
    const Quat q{0.5, 0.1, 0.2, 0.3};
    CHECK(p::to_host(AxisConvention::core, q) == q);
    CHECK(p::to_host(AxisConvention::unity, q) == Quat{0.5, -0.1, -0.3, -0.2});
    CHECK(p::to_host(AxisConvention::godot, q) == Quat{0.5, 0.1, 0.3, -0.2});
    CHECK(p::to_host(AxisConvention::unreal, q) == Quat{0.5, -0.1, 0.2, -0.3});
}

TEST_CASE("mappings agree with the matrix definitions for random rotations", "[presentation][axes]") {
    test::SplitMix64 rng(5);
    for (int n = 0; n < 2000; ++n) {
        const Quat q = test::random_rotation(rng);
        const Vec3 v = test::random_vector(rng, 1.0e9);
        for (const AxisConvention c : k_all) {
            const Mat3 s = p::axis_matrix(c);
            CHECK(p::to_host(c, v) == s * v);
            // R_host = S R S^T; the mapped quaternion must represent exactly that rotation.
            CHECK(m::max_abs_difference(m::to_matrix(p::to_host(c, q)), s * m::to_matrix(q) * m::transpose(s)) <
                  1e-15);
            // Rotating then mapping equals mapping then rotating.
            const Vec3 a = p::to_host(c, m::rotate(q, v));
            const Vec3 b = m::rotate(p::to_host(c, q), p::to_host(c, v));
            CHECK(test::max_abs_difference(a, b) < 1e-5); // 1e9 km vectors: about 1e-14 relative
            // from_host undoes to_host exactly.
            CHECK(p::from_host(c, p::to_host(c, v)) == v);
            CHECK(p::from_host(c, p::to_host(c, q)) == q);
        }
    }
}

TEST_CASE("host rotations are proper rotations", "[presentation][axes]") {
    // A left-handed host still receives a unit quaternion that is a proper rotation in its own axes.
    test::SplitMix64 rng(6);
    for (int n = 0; n < 100; ++n) {
        const Quat q = test::random_rotation(rng);
        for (const AxisConvention c : k_all) {
            CHECK_THAT(m::determinant(m::to_matrix(p::to_host(c, q))), WithinAbs(1.0, 1e-14));
        }
    }
}
