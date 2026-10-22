// The enlarged-bodies scale mode on the default pack (ARCHITECTURE section 5.3): the star keeps its true
// size, planets and moons grow by the factor, and a planet and its moon stay apart.

#include "usim/sim/simulation.hpp"
#include "validation/validation_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <vector>

namespace v = usim::test::validation;
using usim::catalog::BodyId;
using usim::frames::FrameId;

TEST_CASE("enlarged bodies keep the star true and planetary systems apart", "[validation][snapshot]") {
    auto created = usim::sim::Simulation::create(v::default_pack());
    REQUIRE(created.has_value());
    auto& sim = created.value();
    usim::sim::SnapshotRequest request;
    request.center = BodyId::naif(10);
    request.view.km_per_render_unit = 1.0;
    std::vector<usim::sim::BodySnapshot> plain(sim.snapshot_size());
    REQUIRE(sim.fill_snapshot(request, plain).has_value());
    request.view.scale_mode = usim::presentation::ScaleMode::enlarged_bodies;
    request.view.body_scale = 1000.0;
    std::vector<usim::sim::BodySnapshot> enlarged(sim.snapshot_size());
    REQUIRE(sim.fill_snapshot(request, enlarged).has_value());

    const auto index = [&](std::int64_t id) {
        const auto bodies = sim.catalog().bodies();
        for (std::size_t i = 0; i < bodies.size(); ++i) {
            if (bodies[i].id == BodyId::naif(id)) {
                return i;
            }
        }
        FAIL("no body " << id);
        return std::size_t{0};
    };
    const std::size_t sun = index(10);
    const std::size_t earth = index(399);
    const std::size_t moon = index(301);
    const std::size_t mars = index(499);

    CHECK(enlarged[sun].render_radius == plain[sun].render_radius);
    CHECK(enlarged[mars].render_radius == plain[mars].render_radius * 1000.0); // no moons in the v1 catalog

    // The Earth and the Moon are capped at 40 % of their distance each, so they cannot touch.
    const double separation =
        usim::math::distance(enlarged[earth].render_position, enlarged[moon].render_position);
    const double earth_max = std::max(
        {enlarged[earth].render_radii.x, enlarged[earth].render_radii.y, enlarged[earth].render_radii.z});
    const double moon_max = std::max(
        {enlarged[moon].render_radii.x, enlarged[moon].render_radii.y, enlarged[moon].render_radii.z});
    CHECK(earth_max <= 0.4 * separation * (1.0 + 1e-12));
    CHECK(moon_max <= 0.4 * separation * (1.0 + 1e-12));
    CHECK(earth_max + moon_max < separation);
    CHECK(enlarged[earth].render_radius > plain[earth].render_radius * 10.0); // still clearly enlarged
}
