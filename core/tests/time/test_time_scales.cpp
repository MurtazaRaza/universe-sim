#include "usim/time/time_scales.hpp"

#include "fixtures/spice_reference.hpp"
#include "test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>
#include <string>
#include <vector>

using Catch::Matchers::WithinAbs;
using usim::ErrorCode;
using usim::time::Calendar;
using usim::time::DateTime;
using usim::time::Instant;
using usim::time::LeapSecondEntry;
using usim::time::LeapSecondTable;
using usim::time::TaiTime;
using usim::time::TimeScale;
using usim::time::TimeScales;
using usim::time::TtTime;
namespace spice = usim::test::spice;
namespace test = usim::test;

namespace {

// The time scale data from naif0012.tls, as the data pack will provide it from Phase 2 on.
TimeScales naif0012() {
    std::vector<LeapSecondEntry> entries;
    for (const auto& row : spice::k_leap_seconds) {
        entries.push_back({{row.year, row.month, row.day}, row.tai_minus_utc_s});
    }
    return TimeScales(LeapSecondTable::create(entries).value(),
                      {spice::k_tdb_k_s, spice::k_tdb_eb, spice::k_tdb_m0_rad, spice::k_tdb_m1_rad_s});
}

// SPICE returns seconds past J2000 as single doubles, so its results carry the rounding of a double near
// that magnitude. Allow a few units in the last place, and at least 20 picoseconds for our own two-part
// rounding.
double spice_tolerance(double seconds) {
    return std::fmax(4.0 * test::ulp(seconds), 2e-11);
}

} // namespace

TEST_CASE("UTC to TDB matches CSPICE str2et", "[time][scales]") {
    const TimeScales scales = naif0012();
    for (const auto& c : spice::k_utc_cases) {
        INFO(c.utc);
        const auto t = scales.parse(std::string(c.utc) + " UTC");
        REQUIRE(t.has_value());
        CHECK_THAT(t.value().seconds_since_j2000(), WithinAbs(c.tdb_s, spice_tolerance(c.tdb_s)));
    }
}

TEST_CASE("TDB to UTC text matches CSPICE et2utc", "[time][scales]") {
    const TimeScales scales = naif0012();
    for (const auto& c : spice::k_utc_cases) {
        const auto text = scales.format(Instant::from_seconds_since_j2000(c.tdb_s), TimeScale::utc, Calendar::mixed, 6);
        REQUIRE(text.has_value());
        CHECK(text.value() == std::string(c.utc_isoc6) + " UTC");
    }
}

TEST_CASE("TDB, TT and TAI match CSPICE unitim", "[time][scales]") {
    const TimeScales scales = naif0012();
    for (const auto& c : spice::k_tdb_cases) {
        INFO("TDB " << c.tdb_s);
        const Instant tdb = Instant::from_seconds_since_j2000(c.tdb_s);
        const TtTime tt = scales.tt_from_tdb(tdb);
        CHECK_THAT(tt.seconds_since_j2000(), WithinAbs(c.tt_s, spice_tolerance(c.tt_s)));
        CHECK_THAT(usim::time::tai_from_tt(tt).seconds_since_j2000(), WithinAbs(c.tai_s, spice_tolerance(c.tai_s)));

        const Instant back = scales.tdb_from_tt(TtTime::from_seconds_since_j2000(c.tt_s));
        CHECK_THAT(back.seconds_since_j2000(), WithinAbs(c.tdb_from_tt_s, spice_tolerance(c.tdb_from_tt_s)));

        // The inverse is exact to the resolution of the two-part representation.
        CHECK_THAT(scales.tdb_from_tt(tt).seconds_since(tdb), WithinAbs(0.0, 1e-12));
    }
}

TEST_CASE("TDB - TT is the SPICE model evaluated at TT", "[time][scales]") {
    // At J2000 TDB the values are small enough that CSPICE's result has no rounding to speak of.
    const TimeScales scales = naif0012();
    const TtTime tt = scales.tt_from_tdb(Instant());
    CHECK_THAT(-tt.seconds_since_j2000(), WithinAbs(scales.tdb_minus_tt_s(tt), 1e-11));
    CHECK(std::fabs(scales.tdb_minus_tt_s(tt)) < 2e-3);
}

TEST_CASE("UTC leap seconds", "[time][scales]") {
    const TimeScales scales = naif0012();
    const auto& table = scales.leap_seconds();
    const std::int64_t dec31 = usim::time::julian_day_number({2016, 12, 31}, Calendar::gregorian).value();
    CHECK(table.utc_day_length_s(dec31).value() == 86'401);
    CHECK(table.utc_day_length_s(dec31 - 1).value() == 86'400);
    CHECK(table.tai_minus_utc_s(dec31).value() == 36);
    CHECK(table.tai_minus_utc_s(dec31 + 1).value() == 37);
    // After the last entry, no further leap seconds are assumed.
    CHECK(table.tai_minus_utc_s(dec31 + 100'000).value() == 37);

    // 23:59:60 exists only on a day that ends with a leap second.
    const auto wrong_day = scales.parse("2016-12-30T23:59:60 UTC");
    REQUIRE_FALSE(wrong_day.has_value());
    CHECK(wrong_day.error().code == ErrorCode::invalid_argument);

    // Readings across the leap second are strictly increasing and round-trip.
    const Instant start = scales.parse("2016-12-31T23:59:58 UTC").value();
    std::string previous;
    for (int step = 0; step <= 16; ++step) {
        const Instant t = start.plus_seconds(0.25 * step);
        const std::string text = scales.format(t, TimeScale::utc, Calendar::mixed, 9).value();
        CHECK(text > previous);
        previous = text;
        CHECK_THAT(scales.parse(text).value().seconds_since(t), WithinAbs(0.0, 1e-9));
    }
    CHECK(scales.format(start.plus_seconds(2.5), TimeScale::utc).value() == "2016-12-31T23:59:60.500 UTC");
    CHECK(scales.format(start.plus_seconds(3.0), TimeScale::utc).value() == "2017-01-01T00:00:00.000 UTC");
}

TEST_CASE("rounding carries into the leap second and the next day", "[time][scales]") {
    const TimeScales scales = naif0012();
    const auto at = [&](const char* utc, double offset) {
        return scales.parse(utc).value().plus_seconds(offset);
    };
    CHECK(scales.format(at("2016-12-31T23:59:59 UTC", 0.9996), TimeScale::utc).value() ==
          "2016-12-31T23:59:60.000 UTC");
    CHECK(scales.format(at("2016-12-31T23:59:60 UTC", 0.9996), TimeScale::utc).value() ==
          "2017-01-01T00:00:00.000 UTC");
    CHECK(scales.format(at("2016-12-30T23:59:59 UTC", 0.9996), TimeScale::utc).value() ==
          "2016-12-31T00:00:00.000 UTC");
    CHECK(scales.format(at("2000-12-31T23:59:59 TDB", 0.9996), TimeScale::tdb).value() ==
          "2001-01-01T00:00:00.000 TDB");
}

TEST_CASE("UTC is undefined before 1972", "[time][scales]") {
    const TimeScales scales = naif0012();
    const auto before = scales.parse("1971-12-31T23:59:59 UTC");
    REQUIRE_FALSE(before.has_value());
    CHECK(before.error().code == ErrorCode::out_of_range);

    const auto formatted = scales.format(scales.parse("1960-01-01T00:00:00 TDB").value(), TimeScale::utc);
    REQUIRE_FALSE(formatted.has_value());
    CHECK(formatted.error().code == ErrorCode::out_of_range);

    // TDB, TT and TAI have no such limit.
    CHECK(scales.parse("1960-01-01T00:00:00 TT").has_value());
    CHECK(scales.format(scales.parse("-3000-01-01T00:00:00 TDB").value(), TimeScale::tai).has_value());
}

TEST_CASE("readings at J2000.0 on each scale", "[time][scales]") {
    const TimeScales scales = naif0012();
    const Instant j2000;
    CHECK(scales.format(j2000, TimeScale::tdb).value() == "2000-01-01T12:00:00.000 TDB");
    // At J2000, TDB - TT is about -72.7 microseconds (CSPICE unitim gives TT = +7.27e-5 s past J2000).
    CHECK(scales.format(j2000, TimeScale::tt, Calendar::mixed, 6).value() == "2000-01-01T12:00:00.000073 TT");
    CHECK(scales.format(j2000, TimeScale::tai, Calendar::mixed, 6).value() == "2000-01-01T11:59:27.816073 TAI");
    CHECK(scales.format(j2000, TimeScale::utc, Calendar::mixed, 6).value() == "2000-01-01T11:58:55.816073 UTC");
}

TEST_CASE("calendars apply when converting instants", "[time][scales]") {
    const TimeScales scales = naif0012();
    const Instant jd0 = Instant::from_julian_date(0.0);
    CHECK(scales.format(jd0, TimeScale::tdb, Calendar::mixed, 0).value() == "-4712-01-01T12:00:00 TDB");
    CHECK(scales.format(jd0, TimeScale::tdb, Calendar::gregorian, 0).value() == "-4713-11-24T12:00:00 TDB");
    CHECK(scales.parse("-4712-01-01T12:00:00 TDB", Calendar::julian).value() == jd0);

    const auto gap = scales.parse("1582-10-10T00:00:00 TDB", Calendar::mixed);
    REQUIRE_FALSE(gap.has_value());
    CHECK(gap.error().code == ErrorCode::invalid_argument);
}

TEST_CASE("parse and format round-trip on every scale", "[time][scales]") {
    const TimeScales scales = naif0012();
    test::SplitMix64 rng(4);
    for (int n = 0; n < 500; ++n) {
        // From 1972 to about 2150 for UTC; from -3000 to +3000 years for the others.
        const Instant utc_range = Instant::from_seconds_since_j2000(rng.uniform(-8.8e8, 4.7e9));
        const Instant wide = Instant::from_seconds_since_j2000(rng.uniform(-9.5e10, 3.2e10));
        for (const TimeScale scale : {TimeScale::tdb, TimeScale::tt, TimeScale::tai, TimeScale::utc}) {
            const Instant t = scale == TimeScale::utc ? utc_range : wide;
            const std::string text = scales.format(t, scale, Calendar::mixed, 9).value();
            INFO(text);
            CHECK_THAT(scales.parse(text).value().seconds_since(t), WithinAbs(0.0, 1e-9));
        }
    }
}

TEST_CASE("leap second tables are validated", "[time][scales]") {
    CHECK(LeapSecondTable::create({}).error().code == ErrorCode::invalid_argument);
    const LeapSecondEntry out_of_order[] = {{{1980, 1, 1}, 19}, {{1979, 1, 1}, 18}};
    CHECK(LeapSecondTable::create(out_of_order).error().code == ErrorCode::invalid_argument);
    const LeapSecondEntry bad_date[] = {{{1980, 2, 30}, 19}};
    CHECK_FALSE(LeapSecondTable::create(bad_date).has_value());
    const LeapSecondEntry bad_offset[] = {{{1980, 1, 1}, -5}};
    CHECK_FALSE(LeapSecondTable::create(bad_offset).has_value());
}

TEST_CASE("invalid instants and fields are rejected", "[time][scales]") {
    const TimeScales scales = naif0012();
    const Instant invalid = Instant::from_seconds_since_j2000(std::nan(""));
    CHECK(scales.format(invalid, TimeScale::tdb).error().code == ErrorCode::invalid_argument);
    CHECK(scales.to_instant(DateTime{{2000, 1, 1}, 25, 0, 0.0}, TimeScale::tt).error().code ==
          ErrorCode::invalid_argument);
    CHECK(scales.to_instant(DateTime{{2000, 1, 1}, 12, 0, 60.0}, TimeScale::tdb).error().code ==
          ErrorCode::invalid_argument);
    CHECK(scales.to_instant(DateTime{{2000, 1, 1}, 12, 0, std::nan("")}, TimeScale::utc).error().code ==
          ErrorCode::invalid_argument);
}
