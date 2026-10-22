// P3-06, SPEC NFR-03: time for a full snapshot of the v1 bodies from the default pack. Built with every
// preset, registered with ctest (label `benchmark`) only for the release preset, where the timing check
// means something: `ctest --preset release -L benchmark`.

#include "usim/catalog/body_catalog.hpp"
#include "usim/ephemeris/ephemeris_chain.hpp"
#include "usim/sim/simulation.hpp"
#include "validation/validation_support.hpp"

#include <catch2/benchmark/catch_benchmark.hpp>
#include <catch2/catch_test_macros.hpp>

#include <array>
#include <chrono>
#include <cstdio>
#include <vector>

using usim::catalog::BodyId;
using usim::ephemeris::EphemerisChain;
using usim::ephemeris::Evaluation;
using usim::frames::FrameId;
using usim::time::Instant;

namespace {

// SPEC NFR-03: a full snapshot of all v1 bodies in under 0.1 ms.
constexpr double k_snapshot_budget_s = 1.0e-4;

// The catalog bodies that have ephemeris data: what a snapshot evaluates.
std::vector<BodyId> snapshot_bodies(const usim::data::DataPack& pack, const EphemerisChain& chain) {
    std::vector<BodyId> bodies;
    for (const auto& body : pack.catalog().bodies()) {
        if (chain.contains(body.id)) {
            bodies.push_back(body.id);
        }
    }
    return bodies;
}

// One snapshot: every body relative to `center` in ECLIPJ2000 (the default display frame) at `t`.
double snapshot(const EphemerisChain& chain, std::span<const BodyId> bodies, BodyId center, Instant t,
                std::span<Evaluation> out) {
    double checksum = 0.0;
    for (std::size_t i = 0; i < bodies.size(); ++i) {
        out[i] = chain.evaluate(bodies[i], center, FrameId::eclipj2000(), t);
        checksum += out[i].state.position_km.x;
    }
    return checksum;
}

} // namespace

TEST_CASE("ephemeris states of all v1 bodies", "[benchmark]") {
    const auto& pack = usim::test::validation::default_pack();
    const auto chain = EphemerisChain::create(pack);
    REQUIRE(chain.has_value());
    const std::vector<BodyId> bodies = snapshot_bodies(pack, chain.value());
    std::array<Evaluation, 64> out{};
    REQUIRE(bodies.size() <= out.size());

    // Instants spread over the coverage, so the records come from all over the pack.
    std::vector<Instant> instants;
    for (int i = 0; i < 1000; ++i) {
        instants.push_back(Instant::from_seconds_since_j2000(-4.0e9 + 8.0e6 * i));
    }
    BENCHMARK("1000 ephemeris snapshots relative to the SSB") {
        double sum = 0.0;
        for (const Instant t : instants) {
            sum += snapshot(chain.value(), bodies, BodyId::naif(0), t, out);
        }
        return sum;
    };
    BENCHMARK("1000 ephemeris snapshots relative to the Earth") {
        double sum = 0.0;
        for (const Instant t : instants) {
            sum += snapshot(chain.value(), bodies, BodyId::naif(399), t, out);
        }
        return sum;
    };
}

TEST_CASE("NFR-03: a full v1 snapshot fits the time budget", "[benchmark]") {
    // The per-frame host call: the clock advances and fill_snapshot gives positions, orientations and radii
    // of every catalog body, relative to the Earth, in render units and Unity axes.
    const auto path = usim::test::validation::default_pack_path();
    if (!path) {
        SKIP("no default pack in data/packs");
    }
    auto loaded = usim::sim::Simulation::load_file(*path);
    REQUIRE(loaded.has_value());
    usim::sim::Simulation& sim = loaded.value();
    std::array<usim::sim::BodySnapshot, 64> out{};
    REQUIRE(sim.snapshot_size() <= out.size());
    usim::sim::SnapshotRequest request;
    request.center = BodyId::naif(399);
    request.view.km_per_render_unit = 1.0e6;
    request.view.axes = usim::presentation::AxisConvention::unity;
    // About 2 days per frame at 60 frames per second, so frames reach new records often.
    REQUIRE(sim.clock().set_rate(1.0e7).has_value());
    REQUIRE(sim.clock().set_time(Instant::from_seconds_since_j2000(-4.0e9)).has_value());

    BENCHMARK("1000 frames: advance the clock and fill a snapshot") {
        double sum = 0.0;
        for (int frame = 0; frame < 1000; ++frame) {
            (void)sim.clock().advance(1.0 / 60.0);
            sum += static_cast<double>(sim.fill_snapshot(request, out).value_or(0));
            sum += out[3].render_position.x;
        }
        return sum;
    };

    // The budget check: the mean over many frames, measured directly.
    REQUIRE(sim.clock().set_time(Instant::from_seconds_since_j2000(-4.0e9)).has_value());
    constexpr int k_frames = 20000;
    double sum = 0.0;
    const auto start = std::chrono::steady_clock::now();
    for (int frame = 0; frame < k_frames; ++frame) {
        (void)sim.clock().advance(1.0 / 60.0);
        sum += static_cast<double>(sim.fill_snapshot(request, out).value_or(0));
    }
    const std::chrono::duration<double> elapsed = std::chrono::steady_clock::now() - start;
    const double per_snapshot_s = elapsed.count() / k_frames;
    std::printf("NFR-03: %zu bodies per snapshot (positions, orientations, radii), %.3g us per snapshot "
                "(budget %.3g us), checksum %.6g\n",
                sim.snapshot_size(), per_snapshot_s * 1e6, k_snapshot_budget_s * 1e6, sum);
    CHECK(sum == static_cast<double>(k_frames) * static_cast<double>(sim.snapshot_size()));
    CHECK(per_snapshot_s < k_snapshot_budget_s);
}
