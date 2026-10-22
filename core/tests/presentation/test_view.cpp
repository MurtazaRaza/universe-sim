// P4-05: view origin, render units, scale modes and host axes.

#include "test_support.hpp"
#include "usim/presentation/view.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cmath>
#include <limits>

using usim::ErrorCode;
using usim::math::Quat;
using usim::math::Vec3;
using usim::presentation::AxisConvention;
using usim::presentation::ScaleMode;
using usim::presentation::View;
namespace p = usim::presentation;

TEST_CASE("positions are re-centered, scaled and mapped to host axes", "[presentation][view]") {
    View view;
    view.origin_km = {1.0e9, -2.0e9, 3.0e8};
    view.km_per_render_unit = 1.0e3;
    view.axes = AxisConvention::unity;
    REQUIRE(p::validate(view).has_value());
    const Vec3 position{1.0e9 + 4000.0, -2.0e9 + 5000.0, 3.0e8 - 6000.0};
    // Core (x, y, z) relative to the origin is (4, 5, -6) units; Unity is (x, z, y).
    CHECK(p::to_render_position(view, position) == Vec3{4.0, -6.0, 5.0});
    CHECK(p::to_render_velocity(view, Vec3{30.0, 0.0, -1.0}) == Vec3{0.03, -0.001, 0.0});
}

TEST_CASE("re-centering keeps sub-meter detail far from the center", "[presentation][view]") {
    // A camera 6e9 km from the center, looking at a point 1.25 m away: the render position resolves it,
    // which a float of the absolute position (resolution 512 km there) could not.
    View view;
    view.origin_km = {6.0e9, 0.0, 0.0};
    const Vec3 r = p::to_render_position(view, Vec3{6.0e9 + 1.25e-3, 0.0, 0.0});
    CHECK(std::abs(r.x - 1.25e-3) < 1e-6);
    CHECK(static_cast<float>(r.x) != 0.0f);
}

TEST_CASE("radii follow the scale mode", "[presentation][view]") {
    View view;
    view.km_per_render_unit = 100.0;
    CHECK(p::to_render_radius(view, 2500.0) == 25.0);
    view.scale_mode = ScaleMode::enlarged_bodies;
    view.body_scale = 50.0;
    CHECK(p::to_render_radius(view, 2500.0) == 1250.0);
    CHECK(p::to_string(ScaleMode::true_scale) == "true-scale");
    CHECK(p::to_string(ScaleMode::enlarged_bodies) == "enlarged-bodies");
}

TEST_CASE("radii stay positive lengths along the host axes", "[presentation][view]") {
    View view;
    view.km_per_render_unit = 10.0;
    view.axes = AxisConvention::godot; // (x, z, -y)
    CHECK(p::to_render_radii(view, Vec3{30.0, 20.0, 10.0}) == Vec3{3.0, 1.0, 2.0});
    view.axes = AxisConvention::unreal; // (x, -y, z)
    view.scale_mode = ScaleMode::enlarged_bodies;
    view.body_scale = 2.0;
    CHECK(p::to_render_radii(view, Vec3{30.0, 20.0, 10.0}) == Vec3{6.0, 4.0, 2.0});
}

TEST_CASE("rotations are mapped to host axes", "[presentation][view]") {
    usim::test::SplitMix64 rng(42);
    View view;
    view.axes = AxisConvention::godot;
    for (int i = 0; i < 20; ++i) {
        const Quat q = usim::test::random_rotation(rng);
        CHECK(p::to_render_rotation(view, q) == p::to_host(AxisConvention::godot, q));
    }
}

TEST_CASE("invalid views are rejected", "[presentation][view]") {
    const double nan = std::numeric_limits<double>::quiet_NaN();
    const double inf = std::numeric_limits<double>::infinity();
    const auto rejected = [](const View& v) {
        const auto r = p::validate(v);
        return !r.has_value() && r.error().code == ErrorCode::invalid_argument;
    };
    View v;
    v.origin_km.y = nan;
    CHECK(rejected(v));
    v = View{};
    for (const double scale : {0.0, -1.0, inf, nan}) {
        v.km_per_render_unit = scale;
        CHECK(rejected(v));
    }
    v = View{};
    v.body_scale = 0.5;
    CHECK(rejected(v));
    v.body_scale = inf;
    CHECK(rejected(v));
    v = View{};
    v.axes = static_cast<AxisConvention>(9);
    CHECK(rejected(v));
    v = View{};
    v.scale_mode = static_cast<ScaleMode>(9);
    CHECK(rejected(v));
    CHECK(p::validate(View{}).has_value());
}

TEST_CASE("distance compression keeps direction and apparent size and preserves depth order",
          "[presentation][view]") {
    View view;
    CHECK(p::distance_compression(view, 1.0e12) == 1.0); // off by default
    view.compression_distance = 100.0;
    CHECK(p::distance_compression(view, 50.0) == 1.0);
    CHECK(p::distance_compression(view, 100.0) == 1.0);
    const double d = 100.0 * std::exp(2.0); // two e-folds beyond the horizon
    CHECK(std::abs(p::distance_compression(view, d) * d - 300.0) < 1e-12);
    // The compressed distance grows with the true distance, slowly.
    double previous = 0.0;
    for (double x = 1.0; x < 1.0e15; x *= 3.0) {
        const double compressed = p::distance_compression(view, x) * x;
        CHECK(compressed > previous);
        previous = compressed;
    }
    CHECK(previous < 100.0 * 40.0);
    View bad;
    bad.compression_distance = -1.0;
    CHECK_FALSE(p::validate(bad).has_value());
}
