// VAL-04 (SPEC section 9): body orientation from the default pack's IAU rotation elements against CSPICE
// sxform with the same PCK (data/reference/val04_orientation.csv): the rotation from the ICRF to each
// body's IAU frame and its time derivative. The same for the moons of the optional moons pack
// (val04_moons_orientation.csv, P6-03), when that pack has been built.

#include "usim/frames/frame_system.hpp"
#include "validation/validation_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <map>
#include <string>
#include <vector>

namespace v = usim::test::validation;
using usim::catalog::BodyId;
using usim::frames::FrameId;
using usim::frames::FrameSystem;
using usim::math::Mat3;
using usim::time::Instant;

namespace {

// SPEC section 9, VAL-04: orientation within 1 microradian. Matrix elements differ by at most the rotation
// angle for small differences, so the largest element difference bounds it.
constexpr double k_val04_rad = 1.0e-6;

// The rotation rate: within 1e-6 of the rate's own size (the same relative precision as the threshold
// above for a body turning one radian).
constexpr double k_val04_rate_relative = 1.0e-6;

Mat3 read_matrix(const v::Table& table, const std::vector<std::string>& row, const std::string& prefix) {
    Mat3 m;
    for (std::size_t i = 0; i < 3; ++i) {
        for (std::size_t j = 0; j < 3; ++j) {
            m(i, j) = v::parse_double(row[table.column(prefix + std::to_string(i) + std::to_string(j))]);
        }
    }
    return m;
}

double largest_element(const Mat3& m) {
    double worst = 0.0;
    for (const double x : m.m) {
        worst = std::max(worst, std::abs(x));
    }
    return worst;
}

void check_orientations(const usim::data::DataPack& pack, const std::string& reference) {
    const FrameSystem frames(std::vector(pack.rotation_elements().begin(), pack.rotation_elements().end()));
    const auto table = v::read_reference(reference);
    const auto body_col = table.column("body");
    const auto tdb_col = table.column("tdb_s");

    std::map<long long, std::pair<double, double>> worst; // per body: rotation, relative rate
    for (const auto& row : table.rows) {
        const long long body = std::stoll(row[body_col]);
        INFO(row[table.column("frame")] << " at TDB " << row[tdb_col] << " s");
        const Instant t = Instant::from_seconds_since_j2000(v::parse_double(row[tdb_col]));
        const auto transform = frames.transform(FrameId::icrf(), FrameId::body_fixed(BodyId::naif(body)), t);
        REQUIRE(transform.has_value());
        const Mat3 r = read_matrix(table, row, "r");
        const Mat3 dr = read_matrix(table, row, "dr");
        const double dr_error = usim::math::max_abs_difference(transform.value().rotation_rate_per_s(), dr);
        const double r_error = usim::math::max_abs_difference(transform.value().rotation(), r);
        const double rate_scale = largest_element(dr);
        CHECK(r_error <= k_val04_rad);
        CHECK(dr_error <= k_val04_rate_relative * rate_scale);
        auto& w = worst[body];
        w = {std::max(w.first, r_error),
             std::max(w.second, rate_scale > 0.0 ? dr_error / rate_scale : dr_error)};
    }

    double worst_r = 0.0;
    double worst_dr = 0.0;
    for (const auto& [body, w] : worst) {
        std::printf("VAL-04 %4lld: rotation %.3g rad, rate %.3g relative\n", body, w.first, w.second);
        worst_r = std::max(worst_r, w.first);
        worst_dr = std::max(worst_dr, w.second);
    }
    std::printf("VAL-04 (%s): %zu orientations of %zu bodies, largest differences from sxform %.3g rad, rate "
                "%.3g relative\n",
                reference.c_str(), table.rows.size(), worst.size(), worst_r, worst_dr);
}

} // namespace

TEST_CASE("VAL-04: orientations from the pack agree with CSPICE sxform", "[validation][val04]") {
    check_orientations(v::default_pack(), "val04_orientation.csv");
}

TEST_CASE("VAL-04: moon orientations from the moons pack agree with CSPICE sxform", "[validation][val04][moons]") {
    check_orientations(v::moons_pack(), "val04_moons_orientation.csv");
}
