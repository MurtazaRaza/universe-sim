// VAL-02 (SPEC section 9): end-to-end planetary states from the default pack (DE440) against JPL Horizons
// API captures (DE441), 1900 to 2100 (data/reference/horizons/val02_<target>_wrt_0.txt). The differences
// are those between the two ephemerides plus the output rounding of Horizons.

#include "usim/ephemeris/ephemeris_chain.hpp"
#include "validation/validation_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <cstdio>
#include <fstream>
#include <map>
#include <string>
#include <vector>

namespace v = usim::test::validation;
using usim::catalog::BodyId;
using usim::ephemeris::EphemerisChain;
using usim::frames::FrameId;
using usim::frames::StateVector;
using usim::math::Vec3;
using usim::time::Instant;

namespace {

// SPEC section 9, VAL-02: position within 1 m, and within 20 m for the Moon, whose orbit differs most
// between DE440 and DE441 (measured 2026-10-21: 10.3 m; every other body within 0.13 m).
constexpr double k_val02_position_km = 1.0e-3;
constexpr double k_val02_moon_position_km = 2.0e-2;
constexpr long long k_moon = 301;

// The bodies captured from Horizons, all relative to the solar system barycenter.
constexpr long long k_targets[] = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 199, 299, 301, 399};

struct HorizonsRow {
    double jd_tdb;
    StateVector state;
};

std::string trim(const std::string& text) {
    const auto first = text.find_first_not_of(' ');
    const auto last = text.find_last_not_of(' ');
    return first == std::string::npos ? std::string() : text.substr(first, last - first + 1);
}

// The vector table between $$SOE and $$EOE: JDTDB, calendar date, X, Y, Z, VX, VY, VZ (km, km/s, ICRF).
std::vector<HorizonsRow> read_horizons(long long target) {
    const auto path =
        v::data_dir() / "reference" / "horizons" / ("val02_" + std::to_string(target) + "_wrt_0.txt");
    std::ifstream in(path);
    REQUIRE(in.good());
    std::vector<HorizonsRow> rows;
    std::string line;
    bool inside = false;
    while (std::getline(in, line)) {
        if (line.rfind("$$SOE", 0) == 0) {
            inside = true;
        } else if (line.rfind("$$EOE", 0) == 0) {
            break;
        } else if (inside) {
            const auto fields = v::split(line);
            REQUIRE(fields.size() >= 8);
            std::vector<double> numbers;
            for (const std::size_t i : {0u, 2u, 3u, 4u, 5u, 6u, 7u}) {
                numbers.push_back(v::parse_double(trim(fields[i])));
            }
            rows.push_back(
                {numbers[0], {{numbers[1], numbers[2], numbers[3]}, {numbers[4], numbers[5], numbers[6]}}});
        }
    }
    REQUIRE_FALSE(rows.empty());
    return rows;
}

} // namespace

TEST_CASE("VAL-02: planetary states agree with JPL Horizons", "[validation][val02]") {
    const auto& pack = v::default_pack();
    const auto chain = EphemerisChain::create(pack);
    REQUIRE(chain.has_value());

    double worst_all = 0.0;
    std::size_t count = 0;
    for (const long long target : k_targets) {
        double worst_p = 0.0;
        double worst_v = 0.0;
        for (const HorizonsRow& row : read_horizons(target)) {
            INFO(target << " at JD " << row.jd_tdb << " TDB");
            const Instant t = Instant::from_julian_date(row.jd_tdb);
            const auto state = chain.value().state(BodyId::naif(target), BodyId::naif(0), FrameId::icrf(), t);
            REQUIRE(state.has_value());
            const double dp = usim::math::distance(state.value().position_km, row.state.position_km);
            CHECK(dp <= (target == k_moon ? k_val02_moon_position_km : k_val02_position_km));
            worst_p = std::max(worst_p, dp);
            worst_v =
                std::max(worst_v, usim::math::distance(state.value().velocity_km_s, row.state.velocity_km_s));
            ++count;
        }
        std::printf("VAL-02 %4lld wrt SSB: position %.3g km, velocity %.3g km/s\n", target, worst_p, worst_v);
        worst_all = std::max(worst_all, worst_p);
    }
    std::printf("VAL-02: %zu states, largest position difference from Horizons %.3g km\n", count, worst_all);
}

TEST_CASE("VAL-02: the Earth relative to the Sun agrees with JPL Horizons", "[validation][val02]") {
    // Phase 3 acceptance: the Earth relative to the Sun at given TDB instants, against the difference of the
    // two Horizons captures relative to the solar system barycenter.
    const auto& pack = v::default_pack();
    const auto chain = EphemerisChain::create(pack);
    REQUIRE(chain.has_value());
    const auto earth = read_horizons(399);
    const auto sun = read_horizons(10);
    REQUIRE(earth.size() == sun.size());
    double worst = 0.0;
    for (std::size_t i = 0; i < earth.size(); ++i) {
        REQUIRE(earth[i].jd_tdb == sun[i].jd_tdb);
        const Instant t = Instant::from_julian_date(earth[i].jd_tdb);
        const auto state = chain.value().state(BodyId::naif(399), BodyId::naif(10), FrameId::icrf(), t);
        REQUIRE(state.has_value());
        const Vec3 expected = earth[i].state.position_km - sun[i].state.position_km;
        const double dp = usim::math::distance(state.value().position_km, expected);
        CHECK(dp <= k_val02_position_km);
        worst = std::max(worst, dp);
    }
    std::printf("VAL-02 Earth wrt Sun: %zu epochs, largest position difference from Horizons %.3g km\n",
                earth.size(), worst);
}
