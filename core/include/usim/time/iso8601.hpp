#pragma once

#include "usim/result.hpp"
#include "usim/time/calendar.hpp"
#include "usim/time/time_scale.hpp"

#include <string>
#include <string_view>

namespace usim::time {

/// A parsed date and time together with the time scale its text named.
struct ScaledDateTime {
    DateTime date_time; ///< Calendar fields as written (the calendar is chosen by the caller).
    TimeScale scale;    ///< The scale named by the suffix.
};

/// Parses an ISO 8601 date and time with an explicit time scale suffix, for example
/// "2000-01-01T12:00:00 TDB", "1969-07-20T20:17:40.5 UTC" or "-0500-03-01T00:00 TT".
///
/// Grammar: `[+|-]YYYY-MM-DD[(T| )hh:mm[:ss[.fff...]]]` followed by one space and a scale (`TDB`, `TT`,
/// `TAI` or `UTC`), or by `Z` directly after the time, which means UTC.
/// - Years have at least four digits (astronomical numbering, so -0001 is 2 B.C.); a sign is allowed.
/// - A missing time means 00:00:00. Fractions of a second may have any number of digits; digits beyond
///   the 18th are ignored.
/// - Seconds from 60 to below 61 are accepted only for UTC at 23:59 (a leap second).
///
/// Only field ranges are checked here (month 1 to 12, day 1 to 31, hour 0 to 23, minute 0 to 59).
/// Whether the day exists, and whether a leap second occurred, depends on the calendar and the leap second
/// table; TimeScales checks those when converting. Fails with ErrorCode::parse_error.
[[nodiscard]] Result<ScaledDateTime> parse_iso8601(std::string_view text);

/// Formats calendar fields as ISO 8601 with a scale suffix, for example "2000-01-01T12:00:00.000 TDB".
/// Years outside 0 to 9999 get a sign ("-0500", "+10000").
///
/// `fractional_digits` (clamped to 0 to 9) sets the decimals of the seconds, rounded to nearest. If
/// rounding would reach the next minute, the seconds are clamped to the largest value in this minute
/// instead (for example 59.9996 with 3 digits prints as 59.999), so no calendar arithmetic is needed.
/// TimeScales::format rounds before splitting into fields and never hits this case.
[[nodiscard]] std::string format_iso8601(const DateTime& date_time, TimeScale scale, int fractional_digits = 3);

} // namespace usim::time
