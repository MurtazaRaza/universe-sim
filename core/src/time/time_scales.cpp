#include "usim/time/time_scales.hpp"

#include "usim/time/iso8601.hpp"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <utility>

namespace usim::time {

namespace {

constexpr double k_day_s = constants::k_seconds_per_day;
constexpr double k_half_day_s = 0.5 * constants::k_seconds_per_day;

// Julian day number of 2000-01-01, the calendar day that contains J2000.0 (noon).
constexpr std::int64_t k_j2000_day = static_cast<std::int64_t>(constants::k_j2000_jd);

constexpr std::int64_t k_powers_of_ten[] = {1,         10,         100,         1'000,         10'000,
                                            100'000,   1'000'000,  10'000'000,  100'000'000,   1'000'000'000};

struct MidnightReading {
    std::int64_t day; // Julian day number of the calendar day
    double seconds;   // seconds since that day's midnight
};

// Converts a noon-based reading into the calendar day that contains it and the seconds since its midnight.
template <TimeScale S>
MidnightReading split_at_midnight(TimePoint<S> t) noexcept {
    const double s = t.seconds_of_day();
    const std::int64_t day = k_j2000_day + t.days_since_j2000();
    if (s >= k_half_day_s) {
        return {day + 1, s - k_half_day_s}; // exact (Sterbenz)
    }
    return {day, s + k_half_day_s};
}

// The noon-based reading of `seconds` after midnight starting calendar day `day`.
template <TimeScale S>
TimePoint<S> join_at_midnight(std::int64_t day, double seconds) noexcept {
    return TimePoint<S>::from_days_and_seconds(day - k_j2000_day, seconds - k_half_day_s);
}

// Checks the time-of-day fields of a reading on a scale with 86400-second days.
Result<double> uniform_seconds_of_day(const DateTime& dt) {
    if (dt.hour < 0 || dt.hour > 23 || dt.minute < 0 || dt.minute > 59 || !std::isfinite(dt.second) ||
        dt.second < 0.0 || dt.second >= 60.0) {
        return make_error(ErrorCode::invalid_argument,
                          "time of day must be 00:00:00 to 23:59:59.999... (seconds of 60 exist only in UTC)");
    }
    return static_cast<double>(dt.hour) * 3600.0 + static_cast<double>(dt.minute) * 60.0 + dt.second;
}

// Builds calendar fields from a calendar day and a time of day given as whole seconds plus a fraction.
// The last minute of a day that ends with a leap second has 61 seconds (23:59:60).
DateTime make_date_time(std::int64_t day, std::int64_t whole_seconds, double fraction, Calendar calendar) {
    const std::int64_t hour = std::min<std::int64_t>(whole_seconds / 3600, 23);
    const std::int64_t minute = std::min<std::int64_t>((whole_seconds - hour * 3600) / 60, 59);
    const double second = static_cast<double>(whole_seconds - hour * 3600 - minute * 60) + fraction;
    return DateTime{calendar_date(day, calendar), static_cast<std::int32_t>(hour), static_cast<std::int32_t>(minute),
                    second};
}

std::string date_text(CalendarDate date) {
    return std::to_string(date.year) + "-" + std::to_string(date.month) + "-" + std::to_string(date.day);
}

Error before_utc_error() {
    return make_error(ErrorCode::out_of_range,
                      "UTC with leap seconds is defined only from the start of the leap second table "
                      "(1972-01-01); use TDB, TT or TAI for earlier dates");
}

} // namespace

// ---------------------------------------------------------------------------------------------------------
// LeapSecondTable

Result<LeapSecondTable> LeapSecondTable::create(std::span<const LeapSecondEntry> entries) {
    if (entries.empty()) {
        return make_error(ErrorCode::invalid_argument, "the leap second table is empty");
    }
    LeapSecondTable table;
    table.entries_.assign(entries.begin(), entries.end());
    table.day_numbers_.reserve(entries.size());
    for (const LeapSecondEntry& entry : entries) {
        const Result<std::int64_t> day = julian_day_number(entry.effective_date, Calendar::gregorian);
        if (!day) {
            return make_error(ErrorCode::invalid_argument,
                              "leap second table: invalid date " + date_text(entry.effective_date));
        }
        if (!table.day_numbers_.empty() && day.value() <= table.day_numbers_.back()) {
            return make_error(ErrorCode::invalid_argument,
                              "leap second table: dates must be strictly increasing at " +
                                  date_text(entry.effective_date));
        }
        if (entry.tai_minus_utc_s < 0 || entry.tai_minus_utc_s >= 3600) {
            return make_error(ErrorCode::invalid_argument,
                              "leap second table: TAI - UTC out of range at " + date_text(entry.effective_date));
        }
        table.day_numbers_.push_back(day.value());
    }
    return table;
}

std::ptrdiff_t LeapSecondTable::index_for_day(std::int64_t utc_day) const noexcept {
    const auto it = std::upper_bound(day_numbers_.begin(), day_numbers_.end(), utc_day);
    return (it - day_numbers_.begin()) - 1;
}

Result<std::int32_t> LeapSecondTable::tai_minus_utc_s(std::int64_t utc_day) const {
    const std::ptrdiff_t index = index_for_day(utc_day);
    if (index < 0) {
        return before_utc_error();
    }
    return entries_[static_cast<std::size_t>(index)].tai_minus_utc_s;
}

Result<std::int32_t> LeapSecondTable::utc_day_length_s(std::int64_t utc_day) const {
    const std::ptrdiff_t index = index_for_day(utc_day);
    if (index < 0) {
        return before_utc_error();
    }
    const std::ptrdiff_t next = index_for_day(utc_day + 1);
    return 86'400 + entries_[static_cast<std::size_t>(next)].tai_minus_utc_s -
           entries_[static_cast<std::size_t>(index)].tai_minus_utc_s;
}

// ---------------------------------------------------------------------------------------------------------
// TimeScales

TimeScales::TimeScales(LeapSecondTable leap_seconds, TdbModel tdb_model) noexcept
    : leap_seconds_(std::move(leap_seconds)), tdb_model_(tdb_model) {}

double TimeScales::tdb_minus_tt_s(TtTime tt) const noexcept {
    const double m = tdb_model_.m0_rad + tdb_model_.m1_rad_per_s * tt.seconds_since_j2000();
    const double e = m + tdb_model_.eb * std::sin(m);
    return tdb_model_.k_s * std::sin(e);
}

Instant TimeScales::tdb_from_tt(TtTime tt) const noexcept {
    return Instant::from_days_and_seconds(tt.days_since_j2000(), tt.seconds_of_day() + tdb_minus_tt_s(tt));
}

TtTime TimeScales::tt_from_tdb(Instant tdb) const noexcept {
    // Solve tt = tdb - f(tt) with f = tdb_minus_tt_s. |df/dt| is about 3e-10, so each iteration gains
    // about nine digits: two refinements reach the limit of double precision.
    const TtTime first_guess = TtTime::from_days_and_seconds(tdb.days_since_j2000(), tdb.seconds_of_day());
    double delta = tdb_minus_tt_s(first_guess);
    for (int i = 0; i < 2; ++i) {
        delta = tdb_minus_tt_s(first_guess.plus_seconds(-delta));
    }
    return TtTime::from_days_and_seconds(tdb.days_since_j2000(), tdb.seconds_of_day() - delta);
}

Result<TaiTime> TimeScales::tai_from_utc(const DateTime& utc, Calendar calendar) const {
    const Result<std::int64_t> day = julian_day_number(utc.date, calendar);
    if (!day) {
        return day.error();
    }
    const Result<std::int32_t> offset = leap_seconds_.tai_minus_utc_s(day.value());
    if (!offset) {
        return offset.error();
    }
    const std::int32_t day_length = leap_seconds_.utc_day_length_s(day.value()).value();

    const bool fields_ok = utc.hour >= 0 && utc.hour <= 23 && utc.minute >= 0 && utc.minute <= 59 &&
                           std::isfinite(utc.second) && utc.second >= 0.0 &&
                           (utc.second < 60.0 || (utc.hour == 23 && utc.minute == 59));
    const double seconds =
        static_cast<double>(utc.hour) * 3600.0 + static_cast<double>(utc.minute) * 60.0 + utc.second;
    if (!fields_ok || !(seconds < static_cast<double>(day_length))) {
        return make_error(ErrorCode::invalid_argument,
                          "the UTC time does not exist on " + date_text(utc.date) +
                              " (seconds of 60 exist only during a leap second)");
    }
    return join_at_midnight<TimeScale::tai>(day.value(), seconds + static_cast<double>(offset.value()));
}

Result<TimeScales::DayReading> TimeScales::utc_day_reading(TaiTime tai) const {
    if (!tai.is_valid()) {
        return make_error(ErrorCode::invalid_argument, "the time is invalid (non-finite or out of range)");
    }
    const MidnightReading t = split_at_midnight(tai);
    const std::span<const LeapSecondEntry> entries = leap_seconds_.entries();
    const std::span<const std::int64_t> days = leap_seconds_.effective_days();

    // The last entry whose start, read on TAI, is at or before `tai`.
    std::ptrdiff_t index = static_cast<std::ptrdiff_t>(entries.size()) - 1;
    for (; index >= 0; --index) {
        const auto i = static_cast<std::size_t>(index);
        const double since_start = static_cast<double>(t.day - days[i]) * k_day_s +
                                   (t.seconds - static_cast<double>(entries[i].tai_minus_utc_s));
        if (since_start >= 0.0) {
            break;
        }
    }
    if (index < 0) {
        return before_utc_error();
    }

    const auto i = static_cast<std::size_t>(index);
    std::int64_t day = t.day;
    double seconds = t.seconds - static_cast<double>(entries[i].tai_minus_utc_s);
    if (seconds < 0.0) {
        seconds += k_day_s;
        --day;
    }
    // During a positive leap second the reading runs past midnight into the next entry's first day;
    // it belongs to the last day of this entry as 23:59:60.x.
    if (i + 1 < entries.size()) {
        while (day >= days[i + 1]) {
            --day;
            seconds += k_day_s;
        }
    }
    return DayReading{day, seconds};
}

Result<DateTime> TimeScales::utc_from_tai(TaiTime tai, Calendar calendar) const {
    const Result<DayReading> reading = utc_day_reading(tai);
    if (!reading) {
        return reading.error();
    }
    const DayReading r = reading.value();
    const double whole = std::floor(r.seconds);
    return make_date_time(r.day, static_cast<std::int64_t>(whole), r.seconds - whole, calendar);
}

Result<Instant> TimeScales::to_instant(const DateTime& date_time, TimeScale scale, Calendar calendar) const {
    if (scale == TimeScale::utc) {
        const Result<TaiTime> tai = tai_from_utc(date_time, calendar);
        if (!tai) {
            return tai.error();
        }
        return tdb_from_tt(tt_from_tai(tai.value()));
    }

    const Result<std::int64_t> day = julian_day_number(date_time.date, calendar);
    if (!day) {
        return day.error();
    }
    const Result<double> seconds = uniform_seconds_of_day(date_time);
    if (!seconds) {
        return seconds.error();
    }
    switch (scale) {
    case TimeScale::tdb:
        return join_at_midnight<TimeScale::tdb>(day.value(), seconds.value());
    case TimeScale::tt:
        return tdb_from_tt(join_at_midnight<TimeScale::tt>(day.value(), seconds.value()));
    case TimeScale::tai:
        return tdb_from_tt(tt_from_tai(join_at_midnight<TimeScale::tai>(day.value(), seconds.value())));
    case TimeScale::utc:
        break;
    }
    return make_error(ErrorCode::internal, "unhandled time scale");
}

Result<DateTime> TimeScales::to_date_time(Instant t, TimeScale scale, Calendar calendar) const {
    return to_date_time_rounded(t, scale, calendar, -1);
}

Result<DateTime> TimeScales::to_date_time_rounded(Instant t, TimeScale scale, Calendar calendar,
                                                  int fractional_digits) const {
    if (!t.is_valid()) {
        return make_error(ErrorCode::invalid_argument, "the instant is invalid (non-finite or out of range)");
    }

    std::int64_t day = 0;
    double seconds = 0.0;
    std::int64_t day_length = 86'400;
    switch (scale) {
    case TimeScale::tdb: {
        const MidnightReading r = split_at_midnight(t);
        day = r.day;
        seconds = r.seconds;
        break;
    }
    case TimeScale::tt: {
        const MidnightReading r = split_at_midnight(tt_from_tdb(t));
        day = r.day;
        seconds = r.seconds;
        break;
    }
    case TimeScale::tai: {
        const MidnightReading r = split_at_midnight(tai_from_tt(tt_from_tdb(t)));
        day = r.day;
        seconds = r.seconds;
        break;
    }
    case TimeScale::utc: {
        const Result<DayReading> r = utc_day_reading(tai_from_tt(tt_from_tdb(t)));
        if (!r) {
            return r.error();
        }
        day = r.value().day;
        seconds = r.value().seconds;
        day_length = leap_seconds_.utc_day_length_s(day).value();
        break;
    }
    }

    // Whole seconds of the day and the fraction, rounded to the requested digits when asked.
    std::int64_t whole = 0;
    double fraction = 0.0;
    if (fractional_digits >= 0) {
        const std::int64_t unit = k_powers_of_ten[std::clamp(fractional_digits, 0, 9)];
        std::int64_t units = std::llround(seconds * static_cast<double>(unit));
        if (units >= day_length * unit) {
            units -= day_length * unit;
            ++day;
        }
        whole = units / unit;
        fraction = static_cast<double>(units % unit) / static_cast<double>(unit);
    } else {
        const double floor_seconds = std::floor(seconds);
        whole = static_cast<std::int64_t>(floor_seconds);
        fraction = seconds - floor_seconds;
    }

    return make_date_time(day, whole, fraction, calendar);
}

Result<Instant> TimeScales::parse(std::string_view text, Calendar calendar) const {
    const Result<ScaledDateTime> parsed = parse_iso8601(text);
    if (!parsed) {
        return parsed.error();
    }
    return to_instant(parsed.value().date_time, parsed.value().scale, calendar);
}

Result<std::string> TimeScales::format(Instant t, TimeScale scale, Calendar calendar, int fractional_digits) const {
    const int digits = std::clamp(fractional_digits, 0, 9);
    const Result<DateTime> dt = to_date_time_rounded(t, scale, calendar, digits);
    if (!dt) {
        return dt.error();
    }
    return format_iso8601(dt.value(), scale, digits);
}

} // namespace usim::time
