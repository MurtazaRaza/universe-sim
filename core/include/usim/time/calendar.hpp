#pragma once

#include "usim/result.hpp"

#include <compare>
#include <cstdint>

namespace usim::time {

/// Calendar used to label days. Checked against CSPICE (TIMDEF CALENDAR) in the unit tests.
enum class Calendar : std::uint8_t {
    gregorian, ///< Proleptic Gregorian: Gregorian rules for all dates.
    julian,    ///< Proleptic Julian: Julian rules for all dates.
    /// Julian before 1582-10-15, Gregorian from then on. This is the JPL Horizons default and the
    /// project's display default (SPEC section 7.2). The dates 1582-10-05 to 1582-10-14 do not exist in
    /// it; the core rejects them (CSPICE instead reads them as Gregorian dates).
    mixed,
};

/// A calendar day. Years use astronomical numbering: year 0 is 1 B.C., year -1 is 2 B.C.
/// Which calendar the fields belong to is not stored; functions take it as a parameter.
struct CalendarDate {
    std::int32_t year;  ///< Astronomical year number.
    std::int32_t month; ///< 1 to 12.
    std::int32_t day;   ///< 1 to the length of the month.

    /// Field-wise ordering (year, month, day), which is chronological within one calendar.
    friend constexpr auto operator<=>(const CalendarDate&, const CalendarDate&) noexcept = default;
};

/// A calendar day and a time of day, on a time scale and calendar the caller tracks.
struct DateTime {
    CalendarDate date;   ///< The day.
    std::int32_t hour;   ///< 0 to 23.
    std::int32_t minute; ///< 0 to 59.
    /// 0 to below 60. From 60 to below 61 only for a UTC positive leap second (23:59:60).
    double second;

    /// Exact field-wise equality.
    friend constexpr bool operator==(const DateTime&, const DateTime&) noexcept = default;
};

/// Julian day number (the JD at noon) of 1582-10-15 Gregorian, the first Gregorian day of Calendar::mixed.
inline constexpr std::int64_t k_gregorian_reform_julian_day_number = 2'299'161;

/// True if `year` is a leap year in `calendar`. For Calendar::mixed, years up to 1581 use the Julian rule
/// and later years the Gregorian rule (1582 is a common year in both).
[[nodiscard]] bool is_leap_year(std::int32_t year, Calendar calendar) noexcept;

/// Number of days in the month (28 to 31), or 0 if `month` is not 1 to 12. For Calendar::mixed, October
/// 1582 reports 31 although ten of its days do not exist (see is_valid_date()).
[[nodiscard]] std::int32_t days_in_month(std::int32_t year, std::int32_t month, Calendar calendar) noexcept;

/// True if the date exists in `calendar`.
[[nodiscard]] bool is_valid_date(CalendarDate date, Calendar calendar) noexcept;

/// Julian day number of the date: the integer JD at 12:00 on that day (2000-01-01 is 2451545).
/// Fails with ErrorCode::invalid_argument if the date does not exist in `calendar`.
[[nodiscard]] Result<std::int64_t> julian_day_number(CalendarDate date, Calendar calendar);

/// The calendar date whose Julian day number is `julian_day_number`.
/// Precondition: the resulting year fits in 32 bits (|julian_day_number| below about 7.8e11).
[[nodiscard]] CalendarDate calendar_date(std::int64_t julian_day_number, Calendar calendar) noexcept;

} // namespace usim::time
