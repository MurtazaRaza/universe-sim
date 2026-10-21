#include "usim/time/iso8601.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <string_view>

using Catch::Matchers::WithinAbs;
using usim::ErrorCode;
using usim::time::DateTime;
using usim::time::TimeScale;
namespace t = usim::time;

TEST_CASE("parse ISO 8601 with a time scale suffix", "[time][iso8601]") {
    const auto r = t::parse_iso8601("2000-01-01T12:00:00 TDB");
    REQUIRE(r.has_value());
    CHECK(r.value().scale == TimeScale::tdb);
    CHECK(r.value().date_time == DateTime{{2000, 1, 1}, 12, 0, 0.0});

    const auto fraction = t::parse_iso8601("1969-07-20T20:17:40.123456789 TT");
    REQUIRE(fraction.has_value());
    CHECK(fraction.value().scale == TimeScale::tt);
    CHECK_THAT(fraction.value().date_time.second, WithinAbs(40.123456789, 1e-14));

    const auto space = t::parse_iso8601("2010-05-06 07:08 TAI");
    REQUIRE(space.has_value());
    CHECK(space.value().date_time == DateTime{{2010, 5, 6}, 7, 8, 0.0});
    CHECK(space.value().scale == TimeScale::tai);

    const auto date_only = t::parse_iso8601("2010-05-06 UTC");
    REQUIRE(date_only.has_value());
    CHECK(date_only.value().date_time == DateTime{{2010, 5, 6}, 0, 0, 0.0});

    const auto zulu = t::parse_iso8601("2010-05-06T01:02:03Z");
    REQUIRE(zulu.has_value());
    CHECK(zulu.value().scale == TimeScale::utc);

    const auto leap = t::parse_iso8601("2016-12-31T23:59:60.5 UTC");
    REQUIRE(leap.has_value());
    CHECK(leap.value().date_time.second == 60.5);
}

TEST_CASE("parse signed and expanded years", "[time][iso8601]") {
    const auto bc = t::parse_iso8601("-0500-03-01T00:00 TT");
    REQUIRE(bc.has_value());
    CHECK(bc.value().date_time.date.year == -500);

    const auto expanded = t::parse_iso8601("+12000-01-01T00:00:00 TDB");
    REQUIRE(expanded.has_value());
    CHECK(expanded.value().date_time.date.year == 12'000);

    CHECK(t::parse_iso8601("0000-01-01T00:00 TDB").value().date_time.date.year == 0);
}

TEST_CASE("parse rejects malformed text", "[time][iso8601]") {
    constexpr std::string_view k_bad[] = {
        "",
        "2000-01-01T12:00:00",            // no scale
        "2000-01-01T12:00:00 XYZ",        // unknown scale
        "2000-01-01T12:00:00 tdb",        // scales are uppercase
        "2000-01-01T12:00:00  TDB",       // two spaces
        "2000-01-01T12:00:00 TDB extra",  // trailing text
        "200-01-01T12:00 TDB",            // three-digit year
        "2000-1-01T12:00 TDB",            // one-digit month
        "2000-13-01T12:00 TDB",           // month out of range
        "2000-01-32T12:00 TDB",           // day out of range
        "2000-01-01T24:00 TDB",           // hour out of range
        "2000-01-01T12:60 TDB",           // minute out of range
        "2000-01-01T12:00:61 UTC",        // second out of range
        "2016-12-31T23:59:60 TDB",        // leap seconds exist only in UTC
        "2016-12-31T23:58:60 UTC",        // and only at 23:59
        "2000-01-01T12:00:00. TDB",       // decimal point without digits
        "2000-01-01T12 TDB",              // hour without minutes
        "2000-01-01Z",                    // Z needs a time
    };
    for (const std::string_view text : k_bad) {
        INFO("input: \"" << text << "\"");
        const auto r = t::parse_iso8601(text);
        REQUIRE_FALSE(r.has_value());
        CHECK(r.error().code == ErrorCode::parse_error);
    }
}

TEST_CASE("format calendar fields", "[time][iso8601]") {
    const DateTime dt{{2000, 1, 1}, 12, 0, 0.0};
    CHECK(t::format_iso8601(dt, TimeScale::tdb) == "2000-01-01T12:00:00.000 TDB");
    CHECK(t::format_iso8601(dt, TimeScale::tt, 0) == "2000-01-01T12:00:00 TT");
    CHECK(t::format_iso8601({{1969, 7, 20}, 20, 17, 40.123456789}, TimeScale::utc, 9) ==
          "1969-07-20T20:17:40.123456789 UTC");
    CHECK(t::format_iso8601({{-500, 3, 1}, 0, 0, 0.0}, TimeScale::tai, 1) == "-0500-03-01T00:00:00.0 TAI");
    CHECK(t::format_iso8601({{12000, 1, 1}, 0, 0, 0.0}, TimeScale::tdb, 0) == "+12000-01-01T00:00:00 TDB");
    CHECK(t::format_iso8601({{2016, 12, 31}, 23, 59, 60.5}, TimeScale::utc, 3) == "2016-12-31T23:59:60.500 UTC");
}

TEST_CASE("format rounds without carrying into the next minute", "[time][iso8601]") {
    CHECK(t::format_iso8601({{2000, 1, 1}, 0, 0, 1.0005}, TimeScale::tdb, 3) == "2000-01-01T00:00:01.001 TDB");
    CHECK(t::format_iso8601({{2000, 1, 1}, 0, 0, 59.9996}, TimeScale::tdb, 3) == "2000-01-01T00:00:59.999 TDB");
    CHECK(t::format_iso8601({{2016, 12, 31}, 23, 59, 60.9996}, TimeScale::utc, 3) ==
          "2016-12-31T23:59:60.999 UTC");
    // Digits are clamped to 0..9.
    CHECK(t::format_iso8601({{2000, 1, 1}, 0, 0, 1.5}, TimeScale::tdb, -4) == "2000-01-01T00:00:02 TDB");
}

TEST_CASE("format and parse round-trip", "[time][iso8601]") {
    const DateTime dt{{-4712, 1, 1}, 12, 34, 56.789};
    const auto back = t::parse_iso8601(t::format_iso8601(dt, TimeScale::tt, 3));
    REQUIRE(back.has_value());
    CHECK(back.value().scale == TimeScale::tt);
    CHECK(back.value().date_time.date == dt.date);
    CHECK(back.value().date_time.hour == 12);
    CHECK(back.value().date_time.minute == 34);
    CHECK_THAT(back.value().date_time.second, WithinAbs(56.789, 1e-12));
}
