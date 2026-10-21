// VAL-03 (SPEC section 9): time scale conversions with the default pack's leap seconds and TDB - TT model,
// against CSPICE str2et, et2utc and unitim on the same leap seconds kernel (data/reference/val03_*.csv).

#include "usim/time/time_scales.hpp"
#include "validation/validation_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <string>

namespace v = usim::test::validation;
using usim::time::Calendar;
using usim::time::Instant;
using usim::time::TimeScale;
using usim::time::TtTime;

namespace {

// SPEC section 9, VAL-03 (provisional): conversions agree with the reference within 0.1 ms.
constexpr double k_val03_threshold_s = 1.0e-4;

} // namespace

TEST_CASE("VAL-03: UTC to TDB and back agree with CSPICE str2et and et2utc", "[validation][val03]") {
    const auto& pack = v::default_pack();
    const auto scales = pack.time_scales();
    const auto table = v::read_reference("val03_utc_to_tdb.csv");
    const auto utc_col = table.column("utc");
    const auto tdb_col = table.column("tdb_s");
    const auto text_col = table.column("utc_isoc6");

    double worst_s = 0.0;
    for (const auto& row : table.rows) {
        INFO(row[utc_col]);
        const auto parsed = scales.parse(row[utc_col] + " UTC", Calendar::gregorian);
        REQUIRE(parsed.has_value());
        const Instant expected = Instant::from_seconds_since_j2000(v::parse_double(row[tdb_col]));
        const double error_s = std::abs(parsed.value().seconds_since(expected));
        worst_s = std::max(worst_s, error_s);
        CHECK(error_s <= k_val03_threshold_s);

        const auto text = scales.format(expected, TimeScale::utc, Calendar::gregorian, 6);
        REQUIRE(text.has_value());
        CHECK(text.value() == row[text_col] + " UTC");
    }
    std::printf("VAL-03 UTC -> TDB: %zu epochs, largest difference from str2et %.3g s\n", table.rows.size(), worst_s);
}

TEST_CASE("VAL-03: TDB, TT and TAI agree with CSPICE unitim", "[validation][val03]") {
    const auto& pack = v::default_pack();
    const auto scales = pack.time_scales();
    const auto table = v::read_reference("val03_tdb_tt_tai.csv");
    const auto tdb_col = table.column("tdb_s");
    const auto tt_col = table.column("tt_s");
    const auto tai_col = table.column("tai_s");
    const auto back_col = table.column("tdb_from_tt_s");

    double worst_s = 0.0;
    for (const auto& row : table.rows) {
        INFO(row[tdb_col]);
        const Instant tdb = Instant::from_seconds_since_j2000(v::parse_double(row[tdb_col]));
        const TtTime tt_expected = TtTime::from_seconds_since_j2000(v::parse_double(row[tt_col]));
        const auto tai_expected = usim::time::TaiTime::from_seconds_since_j2000(v::parse_double(row[tai_col]));
        const Instant back_expected = Instant::from_seconds_since_j2000(v::parse_double(row[back_col]));

        const TtTime tt = scales.tt_from_tdb(tdb);
        const double errors[] = {
            std::abs(tt.seconds_since(tt_expected)),
            std::abs(usim::time::tai_from_tt(tt).seconds_since(tai_expected)),
            std::abs(scales.tdb_from_tt(tt_expected).seconds_since(back_expected)),
        };
        for (const double e : errors) {
            worst_s = std::max(worst_s, e);
            CHECK(e <= k_val03_threshold_s);
        }
    }
    std::printf("VAL-03 TDB <-> TT <-> TAI: %zu epochs, largest difference from unitim %.3g s\n", table.rows.size(),
                worst_s);
}
