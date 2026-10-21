#include "usim/time/time_point.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>
#include <limits>
#include <type_traits>

using Catch::Matchers::WithinAbs;
using usim::time::Instant;
using usim::time::TtTime;

TEST_CASE("the default time point is J2000.0", "[time][instant]") {
    const Instant t;
    CHECK(t.days_since_j2000() == 0);
    CHECK(t.seconds_of_day() == 0.0);
    CHECK(t.seconds_since_j2000() == 0.0);
    CHECK(t.julian_date().part1 == 2'451'545.0);
    CHECK(t.julian_date().part2 == 0.0);
    CHECK(t.is_valid());
}

TEST_CASE("seconds are normalized into [0, 86400)", "[time][instant]") {
    const Instant before = Instant::from_seconds_since_j2000(-1.0);
    CHECK(before.days_since_j2000() == -1);
    CHECK(before.seconds_of_day() == 86'399.0);

    const Instant after = Instant::from_days_and_seconds(2, 3.5 * 86'400.0);
    CHECK(after.days_since_j2000() == 5);
    CHECK(after.seconds_of_day() == 43'200.0);

    const Instant exact = Instant::from_seconds_since_j2000(86'400.0);
    CHECK(exact.days_since_j2000() == 1);
    CHECK(exact.seconds_of_day() == 0.0);

    // A tiny negative offset rounds to a full day; it must not produce seconds_of_day == 86400.
    const Instant tiny = Instant::from_seconds_since_j2000(-1e-30);
    CHECK(tiny.seconds_of_day() < 86'400.0);
    CHECK(tiny.seconds_of_day() >= 0.0);
}

TEST_CASE("two parts keep precision far from J2000", "[time][instant]") {
    // 10 000 years from J2000: one double of seconds resolves only ~60 microseconds there.
    const Instant far = Instant::from_days_and_seconds(3'652'500, 0.0);
    const Instant later = far.plus_seconds(1e-9);
    CHECK_THAT(later.seconds_since(far), WithinAbs(1e-9, 1e-15));

    // A large shift and back returns to the same instant within a picosecond.
    const Instant start = Instant::from_days_and_seconds(-1'000'000, 1234.5678);
    const Instant round_trip = start.plus_seconds(3.0e10).plus_seconds(-3.0e10);
    CHECK_THAT(round_trip.seconds_since(start), WithinAbs(0.0, 1e-6));
    CHECK(round_trip.days_since_j2000() == start.days_since_j2000());
}

TEST_CASE("Julian dates convert in both directions", "[time][instant]") {
    const Instant noon_next_day = Instant::from_julian_date(2'451'546.0);
    CHECK(noon_next_day.days_since_j2000() == 1);
    CHECK(noon_next_day.seconds_of_day() == 0.0);

    const Instant midnight = Instant::from_julian_date(2'451'545.0, -0.5);
    CHECK(midnight.days_since_j2000() == -1);
    CHECK(midnight.seconds_of_day() == 43'200.0);

    // JD 0 is 4713 B.C. January 1, 12:00 (Julian calendar); negative Julian dates are valid too.
    const Instant jd_zero = Instant::from_julian_date(0.0);
    CHECK(jd_zero.days_since_j2000() == -2'451'545);
    CHECK(jd_zero.julian_date().value() == 0.0);
    const Instant negative = Instant::from_julian_date(-100.25);
    CHECK(negative.days_since_j2000() == -2'451'646);
    CHECK(negative.seconds_of_day() == 0.75 * 86'400.0);
    CHECK(negative.julian_date().value() == -100.25);

    // Splitting the input keeps sub-microsecond precision that a single JD double cannot hold.
    const Instant precise = Instant::from_julian_date(2'460'000.0, 1e-12);
    CHECK_THAT(precise.seconds_of_day(), WithinAbs(1e-12 * 86'400.0, 1e-18));
}

TEST_CASE("ordering and differences", "[time][instant]") {
    const Instant a = Instant::from_seconds_since_j2000(-10.0);
    const Instant b = Instant::from_seconds_since_j2000(5.0);
    CHECK(a < b);
    CHECK(b > a);
    CHECK(a == Instant::from_days_and_seconds(0, -10.0));
    CHECK(b.seconds_since(a) == 15.0);
    CHECK(a.seconds_since(b) == -15.0);
}

TEST_CASE("non-finite or out-of-range input gives an invalid time point", "[time][instant]") {
    const double nan = std::numeric_limits<double>::quiet_NaN();
    const double inf = std::numeric_limits<double>::infinity();
    CHECK_FALSE(Instant::from_seconds_since_j2000(nan).is_valid());
    CHECK_FALSE(Instant::from_seconds_since_j2000(inf).is_valid());
    CHECK_FALSE(Instant::from_seconds_since_j2000(-inf).is_valid());
    CHECK_FALSE(Instant::from_seconds_since_j2000(1e300).is_valid());
    CHECK_FALSE(Instant::from_julian_date(nan).is_valid());
    CHECK_FALSE(Instant().plus_seconds(nan).is_valid());
    CHECK(std::isnan(Instant::from_seconds_since_j2000(nan).seconds_since_j2000()));
    // Invalid time points are unordered.
    const Instant invalid = Instant::from_seconds_since_j2000(nan);
    CHECK_FALSE(invalid < Instant());
    CHECK_FALSE(invalid == invalid);
}

TEST_CASE("time scales are distinct types", "[time][instant]") {
    STATIC_REQUIRE(Instant::scale == usim::time::TimeScale::tdb);
    STATIC_REQUIRE(TtTime::scale == usim::time::TimeScale::tt);
    STATIC_REQUIRE_FALSE(std::is_convertible_v<TtTime, Instant>);
}
