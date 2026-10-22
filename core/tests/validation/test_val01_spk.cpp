// VAL-01 (SPEC section 9): ephemeris states from the default pack against CSPICE spkgeo on the same DE440
// kernel (data/reference/val01_spk_states.csv), for every segment's own pair and for pairs that chain
// through several segments. The same for the optional moons pack and its satellite kernels
// (val01_moons_spk_states.csv, P6-01), when that pack has been built.

#include "usim/ephemeris/ephemeris_chain.hpp"
#include "validation/validation_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <map>
#include <string>
#include <utility>

namespace v = usim::test::validation;
using usim::catalog::BodyId;
using usim::ephemeris::EphemerisChain;
using usim::frames::FrameId;
using usim::time::Instant;

namespace {

// SPEC section 9, VAL-01: position within 1 m, velocity within 1 mm/s.
constexpr double k_val01_position_km = 1.0e-3;
constexpr double k_val01_velocity_km_s = 1.0e-6;

void check_states(const usim::data::DataPack& pack, const std::string& reference) {
    const auto chain = EphemerisChain::create(pack);
    REQUIRE(chain.has_value());
    const auto table = v::read_reference(reference);
    const auto target_col = table.column("target");
    const auto center_col = table.column("center");
    const auto tdb_col = table.column("tdb_s");
    const std::size_t x_col = table.column("x_km");
    const std::size_t vx_col = table.column("vx_km_s");

    // Largest differences per pair: position (km) and velocity (km/s).
    std::map<std::pair<long long, long long>, std::pair<double, double>> worst;
    for (const auto& row : table.rows) {
        const long long target = std::stoll(row[target_col]);
        const long long center = std::stoll(row[center_col]);
        INFO(target << " wrt " << center << " at TDB " << row[tdb_col] << " s");
        const Instant t = Instant::from_seconds_since_j2000(v::parse_double(row[tdb_col]));
        const auto state =
            chain.value().state(BodyId::naif(target), BodyId::naif(center), FrameId::icrf(), t);
        REQUIRE(state.has_value());
        const usim::math::Vec3 position{v::parse_double(row[x_col]), v::parse_double(row[x_col + 1]),
                                        v::parse_double(row[x_col + 2])};
        const usim::math::Vec3 velocity{v::parse_double(row[vx_col]), v::parse_double(row[vx_col + 1]),
                                        v::parse_double(row[vx_col + 2])};
        const double dp = usim::math::distance(state.value().position_km, position);
        const double dv = usim::math::distance(state.value().velocity_km_s, velocity);
        CHECK(dp <= k_val01_position_km);
        CHECK(dv <= k_val01_velocity_km_s);
        auto& w = worst[{target, center}];
        w = {std::max(w.first, dp), std::max(w.second, dv)};
    }

    double worst_p = 0.0;
    double worst_v = 0.0;
    for (const auto& [pair, w] : worst) {
        std::printf("VAL-01 %4lld wrt %4lld: position %.3g km, velocity %.3g km/s\n", pair.first, pair.second,
                    w.first, w.second);
        worst_p = std::max(worst_p, w.first);
        worst_v = std::max(worst_v, w.second);
    }
    std::printf("VAL-01 (%s): %zu states, %zu pairs, largest differences from spkgeo %.3g km and %.3g km/s\n",
                reference.c_str(), table.rows.size(), worst.size(), worst_p, worst_v);
}

} // namespace

TEST_CASE("VAL-01: states from the pack agree with CSPICE spkgeo", "[validation][val01]") {
    check_states(v::default_pack(), "val01_spk_states.csv");
}

TEST_CASE("VAL-01: states from the moons pack agree with CSPICE spkgeo", "[validation][val01][moons]") {
    check_states(v::moons_pack(), "val01_moons_spk_states.csv");
}
