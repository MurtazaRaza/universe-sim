// P4-04 on the default pack: one revolution of every catalog body around its default orbit center, and a
// consistency check of the measured revolution against the two-body period from the pack's own GM values.

#include "usim/sim/simulation.hpp"
#include "validation/validation_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cmath>
#include <cstdio>
#include <numbers>

namespace v = usim::test::validation;
using usim::catalog::BodyId;
using usim::frames::FrameId;
using usim::time::Instant;

namespace {

const usim::sim::Simulation& simulation() {
    static const usim::sim::Simulation sim = [] {
        auto created = usim::sim::Simulation::create(v::default_pack());
        if (!created) {
            FAIL("cannot create the simulation: " << created.error().message);
        }
        return std::move(created).value();
    }();
    return sim;
}

} // namespace

TEST_CASE("every v1 body has an orbit path around its default center", "[validation][orbit]") {
    (void)v::default_pack(); // skips without the pack
    const auto& sim = simulation();
    // J2000, and an instant whose revolution of Pluto has to run up to the end of the coverage (found by
    // the CLI: the step to the limit once rounded past it).
    for (const double seconds : {0.0, 845856069.1824043}) {
        const Instant t = Instant::from_seconds_since_j2000(seconds);
        for (const auto& body : sim.catalog().bodies()) {
            const auto center = sim.default_orbit_center(body.id);
            if (!center) {
                CHECK(body.id == BodyId::naif(0)); // only the root has none
                continue;
            }
            INFO(body.name << " around " << usim::catalog::to_string(center.value()));
            const auto path = sim.orbit_path(body.id, center.value(), FrameId::eclipj2000(), t);
            REQUIRE(path.has_value());
            CHECK(path.value().full_revolution);
            CHECK(path.value().approximate == sim.ephemeris().is_approximate(body.id, center.value()));
            CHECK(path.value().points.size() >= 720);
            std::printf("orbit %-20s around %-10s %6zu points over %10.2f days%s\n", body.name.c_str(),
                        usim::catalog::to_string(center.value()).c_str(), path.value().points.size(),
                        path.value().end.seconds_since(path.value().start) / 86400.0,
                        path.value().approximate ? " (approximate)" : "");
        }
    }
}

TEST_CASE("the revolution of the Earth-Moon barycenter matches its two-body period", "[validation][orbit]") {
    // T = 2 pi sqrt(a^3 / mu) with mu = GM(Sun) + GM(Earth-Moon barycenter) from the pack and a from the
    // vis-viva equation at the instant. The ephemeris includes the other planets, so the two differ
    // slightly; this checks that the sampler finds a revolution of the right length.
    (void)v::default_pack();
    const auto& sim = simulation();
    const BodyId sun = BodyId::naif(10);
    const BodyId emb = BodyId::naif(3);
    const Instant t = Instant::from_seconds_since_j2000(0.0);
    const double mu = *sim.catalog().find(sun)->gm.value + *sim.catalog().find(emb)->gm.value;
    const auto state = sim.state(emb, sun, FrameId::icrf(), t);
    REQUIRE(state.has_value());
    const double r = usim::math::norm(state.value().state.position_km);
    const double v2 = usim::math::norm_squared(state.value().state.velocity_km_s);
    const double a = 1.0 / (2.0 / r - v2 / mu);
    const double kepler_s = 2.0 * std::numbers::pi * std::sqrt(a * a * a / mu);

    const auto path = sim.orbit_path(emb, sun, FrameId::eclipj2000(), t);
    REQUIRE(path.has_value());
    const double measured_s = path.value().end.seconds_since(path.value().start);
    std::printf("Earth-Moon barycenter around the Sun: revolution %.6f days, two-body period %.6f days\n",
                measured_s / 86400.0, kepler_s / 86400.0);
    CHECK(std::abs(measured_s / kepler_s - 1.0) < 1.0e-3);
}
