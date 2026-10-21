#include "usim/time/calendar.hpp"

#include <string>

namespace usim::time {

namespace {

// Day counting follows Howard Hinnant's "chrono-Compatible Low-Level Date Algorithms"
// (days_from_civil and civil_from_days), with years starting on March 1 so the leap day is last.
// The Julian versions use a 4-year cycle (1461 days) in place of the 400-year Gregorian cycle
// (146097 days).

// Julian day numbers of 0000-03-01 in each calendar.
constexpr std::int64_t k_gregorian_march_epoch_jdn = 1'721'120;
constexpr std::int64_t k_julian_march_epoch_jdn = 1'721'118;

constexpr std::int64_t floor_div(std::int64_t a, std::int64_t b) noexcept {
    const std::int64_t q = a / b;
    return (a % b != 0 && ((a < 0) != (b < 0))) ? q - 1 : q;
}

// Day of a March-based year (March 1 = 0) for month 1-12 and day 1-31.
constexpr std::int64_t day_of_march_year(std::int64_t month, std::int64_t day) noexcept {
    const std::int64_t mp = (month + 9) % 12; // March = 0, ..., February = 11
    return (153 * mp + 2) / 5 + day - 1;
}

constexpr std::int64_t gregorian_to_jdn(std::int64_t y, std::int64_t m, std::int64_t d) noexcept {
    y -= m <= 2 ? 1 : 0;
    const std::int64_t era = floor_div(y, 400);
    const std::int64_t yoe = y - era * 400;                                          // [0, 399]
    const std::int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + day_of_march_year(m, d); // [0, 146096]
    return era * 146'097 + doe + k_gregorian_march_epoch_jdn;
}

constexpr std::int64_t julian_to_jdn(std::int64_t y, std::int64_t m, std::int64_t d) noexcept {
    y -= m <= 2 ? 1 : 0;
    const std::int64_t era = floor_div(y, 4);
    const std::int64_t yoe = y - era * 4;                         // [0, 3]
    const std::int64_t doe = yoe * 365 + day_of_march_year(m, d); // [0, 1460]
    return era * 1'461 + doe + k_julian_march_epoch_jdn;
}

// Converts a March-based year (starting March 1 of `year`) and a day of that year (March 1 = 0) into a date.
constexpr CalendarDate march_year_to_date(std::int64_t year, std::int64_t day_of_year) noexcept {
    const std::int64_t mp = (5 * day_of_year + 2) / 153;           // [0, 11]
    const std::int64_t d = day_of_year - (153 * mp + 2) / 5 + 1;   // [1, 31]
    const std::int64_t m = mp < 10 ? mp + 3 : mp - 9;              // [1, 12]
    return {static_cast<std::int32_t>(year + (m <= 2 ? 1 : 0)), static_cast<std::int32_t>(m),
            static_cast<std::int32_t>(d)};
}

constexpr CalendarDate jdn_to_gregorian(std::int64_t jdn) noexcept {
    const std::int64_t z = jdn - k_gregorian_march_epoch_jdn;
    const std::int64_t era = floor_div(z, 146'097);
    const std::int64_t doe = z - era * 146'097;                                          // [0, 146096]
    const std::int64_t yoe = (doe - doe / 1'460 + doe / 36'524 - doe / 146'096) / 365; // [0, 399]
    const std::int64_t doy = doe - (365 * yoe + yoe / 4 - yoe / 100);                    // [0, 365]
    return march_year_to_date(yoe + era * 400, doy);
}

constexpr CalendarDate jdn_to_julian(std::int64_t jdn) noexcept {
    const std::int64_t z = jdn - k_julian_march_epoch_jdn;
    const std::int64_t era = floor_div(z, 1'461);
    const std::int64_t doe = z - era * 1'461;              // [0, 1460]
    const std::int64_t yoe = (doe - doe / 1'460) / 365;    // [0, 3]
    const std::int64_t doy = doe - 365 * yoe;              // [0, 365]
    return march_year_to_date(yoe + era * 4, doy);
}

static_assert(gregorian_to_jdn(2000, 1, 1) == 2'451'545);
static_assert(gregorian_to_jdn(1582, 10, 15) == k_gregorian_reform_julian_day_number);
static_assert(julian_to_jdn(1582, 10, 4) == k_gregorian_reform_julian_day_number - 1);
static_assert(julian_to_jdn(-4712, 1, 1) == 0);

// The first and last dates of the 1582 gap in Calendar::mixed.
constexpr CalendarDate k_gap_first{1582, 10, 5};
constexpr CalendarDate k_gap_last{1582, 10, 14};

bool uses_gregorian_rules(std::int32_t year, Calendar calendar) noexcept {
    switch (calendar) {
    case Calendar::gregorian:
        return true;
    case Calendar::julian:
        return false;
    case Calendar::mixed:
        return year >= 1582;
    }
    return true;
}

} // namespace

bool is_leap_year(std::int32_t year, Calendar calendar) noexcept {
    if (uses_gregorian_rules(year, calendar)) {
        return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
    }
    return year % 4 == 0; // also correct for negative years: -4 % 4 == 0
}

std::int32_t days_in_month(std::int32_t year, std::int32_t month, Calendar calendar) noexcept {
    switch (month) {
    case 1:
    case 3:
    case 5:
    case 7:
    case 8:
    case 10:
    case 12:
        return 31;
    case 4:
    case 6:
    case 9:
    case 11:
        return 30;
    case 2:
        return is_leap_year(year, calendar) ? 29 : 28;
    default:
        return 0;
    }
}

bool is_valid_date(CalendarDate date, Calendar calendar) noexcept {
    if (date.month < 1 || date.month > 12 || date.day < 1 ||
        date.day > days_in_month(date.year, date.month, calendar)) {
        return false;
    }
    return !(calendar == Calendar::mixed && date >= k_gap_first && date <= k_gap_last);
}

Result<std::int64_t> julian_day_number(CalendarDate date, Calendar calendar) {
    if (!is_valid_date(date, calendar)) {
        return make_error(ErrorCode::invalid_argument,
                          "the date " + std::to_string(date.year) + "-" + std::to_string(date.month) + "-" +
                              std::to_string(date.day) + " does not exist in the selected calendar");
    }
    const bool gregorian = calendar == Calendar::gregorian ||
                           (calendar == Calendar::mixed && date > k_gap_last);
    return gregorian ? gregorian_to_jdn(date.year, date.month, date.day)
                     : julian_to_jdn(date.year, date.month, date.day);
}

CalendarDate calendar_date(std::int64_t julian_day_number, Calendar calendar) noexcept {
    const bool gregorian =
        calendar == Calendar::gregorian ||
        (calendar == Calendar::mixed && julian_day_number >= k_gregorian_reform_julian_day_number);
    return gregorian ? jdn_to_gregorian(julian_day_number) : jdn_to_julian(julian_day_number);
}

} // namespace usim::time
