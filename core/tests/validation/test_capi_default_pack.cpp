// The C ABI on the default pack: a full snapshot (21 bodies, more than one internal chunk) equals the C++
// facade, and orbit paths exist for every body around its default center.

#include "usim/sim/simulation.hpp"
#include "usim/usim.h"
#include "validation/validation_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <vector>

namespace v = usim::test::validation;

TEST_CASE("C ABI snapshot and orbit paths on the default pack", "[validation][capi]") {
    const auto path = v::default_pack_path();
    if (!path) {
        SKIP("no default pack in data/packs");
    }
    usim_simulation* handle = nullptr;
    REQUIRE(usim_simulation_load_file(path->string().c_str(), &handle) == USIM_STATUS_OK);
    auto sim = usim::sim::Simulation::create(v::default_pack());
    REQUIRE(sim.has_value());

    usim_instant t{};
    REQUIRE(usim_instant_from_seconds(8.0e8, &t) == USIM_STATUS_OK);
    REQUIRE(usim_clock_set_time(handle, t) == USIM_STATUS_OK);
    REQUIRE(sim.value().clock().set_time(usim::time::Instant::from_seconds_since_j2000(8.0e8)).has_value());

    usim_snapshot_request request{};
    request.center = {USIM_ID_NAMESPACE_NAIF, 0, 399};
    request.frame = {USIM_FRAME_ECLIPJ2000, 0, {0, 0, 0}};
    request.km_per_render_unit = 1.0e6;
    request.axes = USIM_AXES_UNITY;
    request.body_scale = 1.0;
    usim::sim::SnapshotRequest cpp_request;
    cpp_request.center = usim::catalog::BodyId::naif(399);
    cpp_request.view.km_per_render_unit = 1.0e6;
    cpp_request.view.axes = usim::presentation::AxisConvention::unity;

    std::vector<usim_body_snapshot> out(64);
    std::int32_t count = 0;
    REQUIRE(usim_fill_snapshot(handle, &request, out.data(), 64, &count) == USIM_STATUS_OK);
    std::vector<usim::sim::BodySnapshot> cpp(sim.value().snapshot_size());
    REQUIRE(sim.value().fill_snapshot(cpp_request, cpp).has_value());
    REQUIRE(count == static_cast<std::int32_t>(cpp.size()));
    REQUIRE(count > 16);
    for (std::size_t i = 0; i < cpp.size(); ++i) {
        CHECK(out[i].id.value == cpp[i].body.value);
        CHECK(out[i].render_position.x == cpp[i].render_position.x);
        CHECK(out[i].render_position.z == cpp[i].render_position.z);
        CHECK(out[i].orientation.x == cpp[i].orientation.x);
        CHECK(out[i].render_radius == cpp[i].render_radius);
        CHECK(out[i].flags == cpp[i].flags);
    }

    std::vector<usim_vec3d> points(16384);
    for (std::int32_t i = 0; i < count; ++i) {
        usim_body_id center{};
        if (usim_default_orbit_center(handle, out[static_cast<std::size_t>(i)].id, &center) !=
            USIM_STATUS_OK) {
            CHECK(out[static_cast<std::size_t>(i)].id.value == 0); // the root
            continue;
        }
        std::int32_t n = 0;
        std::uint32_t flags = 0;
        REQUIRE(usim_orbit_path(handle, out[static_cast<std::size_t>(i)].id, center, &request, points.data(),
                                static_cast<std::int32_t>(points.size()), &n, &flags) == USIM_STATUS_OK);
        CHECK(n > 700);
        CHECK((flags & USIM_ORBIT_FULL_REVOLUTION) != 0);
        CHECK(((flags & USIM_ORBIT_APPROXIMATE) != 0) ==
              ((out[static_cast<std::size_t>(i)].flags & USIM_SNAPSHOT_APPROXIMATE) != 0));
    }
    usim_simulation_destroy(handle);
}
