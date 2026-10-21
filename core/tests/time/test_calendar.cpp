#include "usim/time/calendar.hpp"

#include "fixtures/spice_reference.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>

using usim::ErrorCode;
using usim::time::Calendar;
using usim::time::CalendarDate;
namespace t = usim::time;
namespace spice = usim::test::spice;

namespace {

constexpr std::array<Calendar, 3> k_calendars{Calendar::gregorian, Calendar::julian, Calendar::mixed};

bool in_reform_gap(const CalendarDate& d) {
    return d.year == 1582 && d.month == 10 && d.day >= 5 && d.day <= 14;
}

} // namespace

TEST_CASE("Julian day numbers match CSPICE in all three calendars", "[time][calendar]") {
    for (const auto& c : spice::k_calendar_cases) {
        const Calendar calendar = k_calendars[static_cast<std::size_t>(c.calendar)];
        const CalendarDate date{c.year, c.month, c.day};
        INFO("calendar " << c.calendar << " date " << c.year << "-" << c.month << "-" << c.day);

        // CSPICE reads the 1582 gap dates in the mixed calendar as Gregorian dates; the core rejects them.
        if (calendar == Calendar::mixed && in_reform_gap(date)) {
            const auto r = t::julian_day_number(date, calendar);
            REQUIRE_FALSE(r.has_value());
            CHECK(r.error().code == ErrorCode::invalid_argument);
            continue;
        }

        const auto jdn = t::julian_day_number(date, calendar);
        REQUIRE(jdn.has_value() == c.valid);
        if (c.valid) {
            CHECK(jdn.value() == c.julian_day_number);
            CHECK(t::calendar_date(jdn.value(), calendar) == date);
        } else {
            CHECK(jdn.error().code == ErrorCode::invalid_argument);
        }
    }
}

TEST_CASE("calendar reform boundaries", "[time][calendar]") {
    CHECK(t::julian_day_number({1582, 10, 15}, Calendar::gregorian).value() == t::k_gregorian_reform_julian_day_number);
    // In the mixed calendar, 1582-10-04 (Julian) is followed directly by 1582-10-15 (Gregorian).
    const std::int64_t last_julian = t::julian_day_number({1582, 10, 4}, Calendar::mixed).value();
    CHECK(t::julian_day_number({1582, 10, 15}, Calendar::mixed).value() == last_julian + 1);
    CHECK(t::calendar_date(last_julian + 1, Calendar::mixed) == CalendarDate{1582, 10, 15});
    CHECK(t::calendar_date(last_julian, Calendar::mixed) == CalendarDate{1582, 10, 4});
    for (int day = 5; day <= 14; ++day) {
        CHECK_FALSE(t::is_valid_date({1582, 10, day}, Calendar::mixed));
        CHECK(t::is_valid_date({1582, 10, day}, Calendar::gregorian));
        CHECK(t::is_valid_date({1582, 10, day}, Calendar::julian));
    }
}

TEST_CASE("conversions round-trip over a long span in every calendar", "[time][calendar]") {
    // From before JD 0 to the year 9000, every 7th day, plus every day around the reform.
    for (const Calendar calendar : k_calendars) {
        for (std::int64_t jdn = -1'000'000; jdn < 5'000'000; jdn += 7) {
            const CalendarDate d = t::calendar_date(jdn, calendar);
            REQUIRE(t::is_valid_date(d, calendar));
            REQUIRE(t::julian_day_number(d, calendar).value() == jdn);
        }
        for (std::int64_t jdn = 2'299'000; jdn < 2'299'400; ++jdn) {
            const CalendarDate d = t::calendar_date(jdn, calendar);
            REQUIRE(t::julian_day_number(d, calendar).value() == jdn);
        }
    }
}

TEST_CASE("leap years", "[time][calendar]") {
    CHECK(t::is_leap_year(2000, Calendar::gregorian));
    CHECK_FALSE(t::is_leap_year(1900, Calendar::gregorian));
    CHECK(t::is_leap_year(1900, Calendar::julian));
    CHECK_FALSE(t::is_leap_year(1900, Calendar::mixed));
    CHECK(t::is_leap_year(1500, Calendar::mixed));
    CHECK_FALSE(t::is_leap_year(1582, Calendar::mixed));
    // Astronomical numbering: year 0 (1 B.C.) and year -4 (5 B.C.) are leap years in both calendars.
    CHECK(t::is_leap_year(0, Calendar::gregorian));
    CHECK(t::is_leap_year(0, Calendar::julian));
    CHECK(t::is_leap_year(-4, Calendar::gregorian));
    CHECK_FALSE(t::is_leap_year(-1, Calendar::julian));
    CHECK_FALSE(t::is_leap_year(-100, Calendar::gregorian));
    CHECK(t::is_leap_year(-100, Calendar::julian));
}

TEST_CASE("invalid dates are rejected", "[time][calendar]") {
    CHECK(t::days_in_month(2001, 13, Calendar::gregorian) == 0);
    CHECK(t::days_in_month(2001, 0, Calendar::gregorian) == 0);
    CHECK_FALSE(t::is_valid_date({2001, 2, 29}, Calendar::gregorian));
    CHECK_FALSE(t::is_valid_date({2001, 4, 31}, Calendar::gregorian));
    CHECK_FALSE(t::is_valid_date({2001, 1, 0}, Calendar::gregorian));
    CHECK(t::is_valid_date({2004, 2, 29}, Calendar::gregorian));
    const auto r = t::julian_day_number({2001, 2, 29}, Calendar::gregorian);
    REQUIRE_FALSE(r.has_value());
    CHECK(r.error().code == ErrorCode::invalid_argument);
}
