#pragma once

#include "usim/constants.hpp"
#include "usim/time/time_scale.hpp"

#include <cmath>
#include <compare>
#include <cstdint>
#include <limits>

namespace usim::time {

/// Julian date in two parts. The JD is part1 + part2; keeping the parts separate preserves precision
/// (for example part1 = whole days, part2 = fraction of a day).
struct JulianDate {
    double part1; ///< Usually the whole-day part, in days.
    double part2; ///< Usually the fraction of a day, in days.

    /// The JD as one double (resolution about 40 microseconds near the present).
    [[nodiscard]] constexpr double value() const noexcept { return part1 + part2; }
};

namespace detail {

/// Seconds in a day as an integer.
inline constexpr std::int64_t k_seconds_per_day_int = 86'400;

/// Largest supported distance from J2000.0 in days (about 2.7 billion years).
inline constexpr double k_max_abs_days = 1.0e12;

/// Whole days plus seconds of day in [0, 86400). seconds is NaN when the value is invalid.
struct DaySeconds {
    std::int64_t days;
    double seconds;
};

/// Normalizes days + seconds so that seconds lies in [0, 86400). Non-finite input, or a result more
/// than k_max_abs_days from the epoch, gives {0, NaN}.
[[nodiscard]] inline DaySeconds normalize_day_seconds(std::int64_t days, double seconds) noexcept {
    constexpr double k_day = constants::k_seconds_per_day;
    const DaySeconds invalid{0, std::numeric_limits<double>::quiet_NaN()};
    if (!std::isfinite(seconds) || std::fabs(static_cast<double>(days)) > k_max_abs_days) {
        return invalid;
    }
    const double extra_days = std::floor(seconds / k_day);
    if (std::fabs(extra_days) > k_max_abs_days) {
        return invalid;
    }
    // extra_days * k_day is exact, and so is the difference while |seconds| < 2^53.
    double rest = seconds - extra_days * k_day;
    std::int64_t total_days = days + static_cast<std::int64_t>(extra_days);
    // The division can round across a day boundary, and a tiny negative rest can round up to a full day.
    if (rest < 0.0) {
        rest += k_day;
        --total_days;
    }
    if (rest >= k_day) {
        rest -= k_day;
        ++total_days;
    }
    if (std::fabs(static_cast<double>(total_days)) > k_max_abs_days) {
        return invalid;
    }
    return {total_days, rest};
}

} // namespace detail

/// A reading of the uniform time scale `Scale` (TDB, TT or TAI), stored in two parts: whole days since
/// the J2000.0 epoch of that scale (2000-01-01T12:00:00 on the same scale) and seconds into that day.
/// The two parts keep sub-nanosecond resolution over millions of years, which one double of seconds
/// cannot (SPEC section 7.2).
///
/// Days run from noon to noon, like Julian dates. Seconds of day are always in [0, 86400).
///
/// Factories never have undefined behavior: non-finite input, or input more than about 2.7 billion
/// years from J2000, produces an invalid time point (is_valid() is false, seconds are NaN), and NaN then
/// propagates through arithmetic like any floating-point error. Validate external input before it gets
/// here; the C ABI does.
template <TimeScale Scale>
class TimePoint {
    static_assert(Scale != TimeScale::utc, "UTC is not a uniform scale; represent UTC readings as DateTime");

public:
    /// The scale this time point is read on.
    static constexpr TimeScale scale = Scale;

    /// The J2000.0 epoch of this scale.
    constexpr TimePoint() noexcept = default;

    /// The time point `seconds` after J2000.0 (negative for earlier). For TDB this is SPICE ET.
    [[nodiscard]] static TimePoint from_seconds_since_j2000(double seconds) noexcept {
        return from_days_and_seconds(0, seconds);
    }

    /// The time point `days` whole days plus `seconds` after J2000.0. `seconds` may be any finite value;
    /// it is normalized into [0, 86400).
    [[nodiscard]] static TimePoint from_days_and_seconds(std::int64_t days, double seconds) noexcept {
        const detail::DaySeconds n = detail::normalize_day_seconds(days, seconds);
        return TimePoint(n.days, n.seconds);
    }

    /// The time point at Julian date part1 + part2 on this scale. Pass the whole days in one part and
    /// the fraction in the other to keep full precision.
    [[nodiscard]] static TimePoint from_julian_date(double part1, double part2 = 0.0) noexcept {
        if (!std::isfinite(part1) || !std::isfinite(part2) || std::fabs(part1) > detail::k_max_abs_days ||
            std::fabs(part2) > detail::k_max_abs_days) {
            return TimePoint(0, std::numeric_limits<double>::quiet_NaN());
        }
        const double d1 = part1 - constants::k_j2000_jd;
        const double whole1 = std::floor(d1);
        const double whole2 = std::floor(part2);
        const double fraction = (d1 - whole1) + (part2 - whole2); // in [0, 2)
        return from_days_and_seconds(static_cast<std::int64_t>(whole1) + static_cast<std::int64_t>(whole2),
                                     fraction * constants::k_seconds_per_day);
    }

    /// Whole days since J2000.0 (noon-based days). Undefined content if !is_valid().
    [[nodiscard]] constexpr std::int64_t days_since_j2000() const noexcept { return days_; }

    /// Seconds into the current noon-based day, in [0, 86400). NaN if !is_valid().
    [[nodiscard]] constexpr double seconds_of_day() const noexcept { return seconds_; }

    /// Seconds since J2000.0 as one double. Its resolution degrades with distance from the epoch (about
    /// 0.1 microseconds at 30 years, 60 microseconds at 15 000 years); prefer the two parts, or
    /// seconds_since(), for precise differences.
    [[nodiscard]] double seconds_since_j2000() const noexcept {
        return static_cast<double>(days_) * constants::k_seconds_per_day + seconds_;
    }

    /// The Julian date on this scale, split as {2451545 + days, seconds of day / 86400}.
    [[nodiscard]] JulianDate julian_date() const noexcept {
        return {constants::k_j2000_jd + static_cast<double>(days_), seconds_ / constants::k_seconds_per_day};
    }

    /// False if this time point came from non-finite or out-of-range input.
    [[nodiscard]] bool is_valid() const noexcept { return !std::isnan(seconds_); }

    /// This time point shifted by `seconds` (negative to go back). Large shifts keep full precision.
    [[nodiscard]] TimePoint plus_seconds(double seconds) const noexcept {
        const detail::DaySeconds shift = detail::normalize_day_seconds(0, seconds);
        return from_days_and_seconds(days_ + shift.days, seconds_ + shift.seconds);
    }

    /// Seconds from `earlier` to this time point (negative if this one is earlier), computed from the two
    /// parts so the difference keeps full precision.
    [[nodiscard]] double seconds_since(const TimePoint& earlier) const noexcept {
        return static_cast<double>(days_ - earlier.days_) * constants::k_seconds_per_day +
               (seconds_ - earlier.seconds_);
    }

    /// Chronological ordering. Invalid time points are unordered.
    friend constexpr std::partial_ordering operator<=>(const TimePoint&, const TimePoint&) noexcept = default;

    /// Exact equality of both parts.
    friend constexpr bool operator==(const TimePoint&, const TimePoint&) noexcept = default;

private:
    constexpr TimePoint(std::int64_t days, double seconds) noexcept : days_(days), seconds_(seconds) {}

    std::int64_t days_ = 0;
    double seconds_ = 0.0;
};

/// The simulation's time: a TDB reading. TDB seconds past J2000 equal SPICE ephemeris time (ET).
using Instant = TimePoint<TimeScale::tdb>;

/// A Terrestrial Time reading.
using TtTime = TimePoint<TimeScale::tt>;

/// An International Atomic Time reading.
using TaiTime = TimePoint<TimeScale::tai>;

} // namespace usim::time
