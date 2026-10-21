#pragma once

#include "usim/constants.hpp"
#include "usim/result.hpp"
#include "usim/time/calendar.hpp"
#include "usim/time/time_point.hpp"
#include "usim/time/time_scale.hpp"

#include <cstdint>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace usim::time {

/// One row of the leap second table: from 00:00:00 UTC on `effective_date` (Gregorian), TAI - UTC equals
/// `tai_minus_utc_s` seconds. This is the content of DELTET/DELTA_AT in a NAIF leap seconds kernel and of
/// IERS Bulletin C.
struct LeapSecondEntry {
    CalendarDate effective_date; ///< UTC date (Gregorian) the value takes effect, at 00:00:00.
    std::int32_t tai_minus_utc_s; ///< TAI - UTC in whole seconds from that date on.
};

/// The leap second table, injected as data (from the data pack from Phase 2 on). Immutable.
///
/// UTC with leap seconds exists only from the first entry (1972-01-01 in the NAIF kernels). After the
/// last entry the table assumes no further leap seconds, as SPICE does; dates beyond the next IERS
/// announcement may therefore be off by future leap seconds.
class LeapSecondTable {
public:
    /// Builds a table from entries in chronological order. Fails with ErrorCode::invalid_argument if the
    /// list is empty, a date is not a valid Gregorian date, the dates are not strictly increasing, or a
    /// TAI - UTC value is outside 0 to 3599 s (a sanity bound; the real values are 10 to 37 s).
    [[nodiscard]] static Result<LeapSecondTable> create(std::span<const LeapSecondEntry> entries);

    /// The entries, in chronological order.
    [[nodiscard]] std::span<const LeapSecondEntry> entries() const noexcept { return entries_; }

    /// Julian day numbers of the entries' effective dates, in the same order as entries().
    [[nodiscard]] std::span<const std::int64_t> effective_days() const noexcept { return day_numbers_; }

    /// Julian day number of the first UTC day the table covers.
    [[nodiscard]] std::int64_t first_julian_day_number() const noexcept { return day_numbers_.front(); }

    /// TAI - UTC in seconds during the UTC day with Julian day number `utc_day`.
    /// Fails with ErrorCode::out_of_range before the first entry.
    [[nodiscard]] Result<std::int32_t> tai_minus_utc_s(std::int64_t utc_day) const;

    /// Length in SI seconds of the UTC day `utc_day`: 86400, plus 1 if a leap second ends it (or minus 1
    /// for a negative leap second). Fails with ErrorCode::out_of_range before the first entry.
    [[nodiscard]] Result<std::int32_t> utc_day_length_s(std::int64_t utc_day) const;

private:
    LeapSecondTable() = default;

    // Index of the last entry in effect on `utc_day`, or -1 before the first.
    [[nodiscard]] std::ptrdiff_t index_for_day(std::int64_t utc_day) const noexcept;

    std::vector<LeapSecondEntry> entries_;
    std::vector<std::int64_t> day_numbers_; // Julian day number of each entry's effective date
};

/// Parameters of the TDB - TT model SPICE uses (routine DELTET; Moyer 1981, Celestial Mechanics 23, 33):
///
///     TDB - TT = k sin(E),   E = M + eb sin(M),   M = m0 + m1 t.
///
/// The values come from the leap seconds kernel (DELTET/K, DELTET/EB and DELTET/M), whose header states
/// the model is accurate to about 30 microseconds. The kernel calls t "ephemeris seconds past J2000";
/// CSPICE's unitim evaluates it at TT, and so does the core (bit-identical to unitim in the unit tests).
/// The two readings of t change the result by less than 1e-12 s. The model is used for parity with
/// SPICE, the validation oracle (P1-04).
struct TdbModel {
    double k_s;           ///< Amplitude k, seconds (DELTET/K).
    double eb;            ///< Eccentricity eb of the Earth-Moon barycenter orbit (DELTET/EB).
    double m0_rad;        ///< Mean anomaly at J2000, radians (DELTET/M, first value).
    double m1_rad_per_s;  ///< Mean anomaly rate, radians per second (DELTET/M, second value).
};

/// TAI reading for a TT reading: TAI = TT - 32.184 s (exact, IAU 1991).
[[nodiscard]] inline TaiTime tai_from_tt(TtTime tt) noexcept {
    return TaiTime::from_days_and_seconds(tt.days_since_j2000(), tt.seconds_of_day() - constants::k_tt_minus_tai_s);
}

/// TT reading for a TAI reading: TT = TAI + 32.184 s (exact, IAU 1991).
[[nodiscard]] inline TtTime tt_from_tai(TaiTime tai) noexcept {
    return TtTime::from_days_and_seconds(tai.days_since_j2000(),
                                         tai.seconds_of_day() + constants::k_tt_minus_tai_s);
}

/// Conversions between TDB, TT, TAI and UTC, and between instants and calendar text, using an injected
/// leap second table and TDB model (SPEC FR-SIM-08). Immutable and safe to share between threads.
class TimeScales {
public:
    /// A converter using the given data.
    TimeScales(LeapSecondTable leap_seconds, TdbModel tdb_model) noexcept;

    /// The leap second table in use.
    [[nodiscard]] const LeapSecondTable& leap_seconds() const noexcept { return leap_seconds_; }

    /// The TDB - TT model in use.
    [[nodiscard]] const TdbModel& tdb_model() const noexcept { return tdb_model_; }

    /// TDB - TT in seconds at the TT reading `tt` (at most about 1.7 ms in magnitude).
    [[nodiscard]] double tdb_minus_tt_s(TtTime tt) const noexcept;

    /// The TDB instant of a TT reading. Matches SPICE unitim(TDT -> TDB).
    [[nodiscard]] Instant tdb_from_tt(TtTime tt) const noexcept;

    /// The TT reading at a TDB instant: the inverse of tdb_from_tt(), solved by fixed-point iteration.
    /// Matches SPICE unitim(TDB -> TDT).
    [[nodiscard]] TtTime tt_from_tdb(Instant tdb) const noexcept;

    /// The TAI reading of a UTC date and time in `calendar`. A second of 60 is accepted only at 23:59 of a
    /// day that ends with a leap second. Fails with ErrorCode::invalid_argument for a non-existent date,
    /// time or leap second, and ErrorCode::out_of_range before the leap second table starts (1972).
    [[nodiscard]] Result<TaiTime> tai_from_utc(const DateTime& utc, Calendar calendar = Calendar::mixed) const;

    /// The UTC date and time of a TAI reading, in `calendar`; during a leap second it reads 23:59:60.x.
    /// Fails with ErrorCode::out_of_range before the leap second table starts.
    [[nodiscard]] Result<DateTime> utc_from_tai(TaiTime tai, Calendar calendar = Calendar::mixed) const;

    /// The instant at which the clock of `scale` reads `date_time` in `calendar`.
    /// Fails like tai_from_utc() for UTC, and with ErrorCode::invalid_argument for a non-existent date or
    /// time on the other scales.
    [[nodiscard]] Result<Instant> to_instant(const DateTime& date_time, TimeScale scale,
                                             Calendar calendar = Calendar::mixed) const;

    /// The reading of the clock of `scale` at instant `t`, in `calendar`. Fails with
    /// ErrorCode::out_of_range for UTC before 1972 and ErrorCode::invalid_argument for an invalid `t`.
    [[nodiscard]] Result<DateTime> to_date_time(Instant t, TimeScale scale,
                                                Calendar calendar = Calendar::mixed) const;

    /// Parses ISO 8601 text with a scale suffix (see parse_iso8601()) into an instant.
    [[nodiscard]] Result<Instant> parse(std::string_view text, Calendar calendar = Calendar::mixed) const;

    /// Formats instant `t` as ISO 8601 on `scale`, for example "2000-01-01T11:58:55.816 UTC". Seconds are
    /// rounded to `fractional_digits` (0 to 9); the rounding carries correctly into minutes, days and
    /// leap seconds. Fails like to_date_time().
    [[nodiscard]] Result<std::string> format(Instant t, TimeScale scale, Calendar calendar = Calendar::mixed,
                                             int fractional_digits = 3) const;

private:
    // A reading split at midnight: Julian day number of the calendar day and seconds since its midnight.
    struct DayReading {
        std::int64_t day;
        double seconds;
    };

    [[nodiscard]] Result<DayReading> utc_day_reading(TaiTime tai) const;
    [[nodiscard]] Result<DateTime> to_date_time_rounded(Instant t, TimeScale scale, Calendar calendar,
                                                        int fractional_digits) const;

    LeapSecondTable leap_seconds_;
    TdbModel tdb_model_;
};

} // namespace usim::time
