#include "usim/ephemeris/chebyshev_segment.hpp"

#include "usim/constants.hpp"
#include "usim/math/chebyshev.hpp"

#include <cmath>
#include <span>
#include <string>

namespace usim::ephemeris {

namespace {

// A record may be used up to this far outside [MID - RADIUS, MID + RADIUS]: record boundaries computed in
// floating point do not land exactly on the boundaries of the record intervals.
constexpr double k_record_slack_s = 1.0e-6;

} // namespace

ChebyshevSegment::ChebyshevSegment(const data::EphemerisSegment& segment) noexcept
    : segment_(&segment), coverage_{time::Instant::from_seconds_since_j2000(segment.start_tdb_s),
                                    time::Instant::from_seconds_since_j2000(segment.end_tdb_s)},
      init_(time::Instant::from_seconds_since_j2000(segment.init_tdb_s)),
      coefficients_(segment.degree() + 1u) {}

Result<ChebyshevSegment> ChebyshevSegment::create(const data::EphemerisSegment& segment) {
    const std::string what = "segment " + catalog::to_string(segment.target) + " wrt " +
                             catalog::to_string(segment.center) + " (" + segment.source.locator + ")";
    const std::size_t size = segment.record_size;
    if (segment.records.size() != size * segment.record_count ||
        (segment.spk_type != 2 && segment.spk_type != 3)) {
        return make_error(ErrorCode::data_format, what + ": records do not match the segment's layout");
    }
    for (std::size_t i = 0; i < segment.record_count; ++i) {
        const double mid = segment.records[i * size];
        const double radius = segment.records[i * size + 1];
        // The part of the record's interval [init + i interval, init + (i + 1) interval] that coverage uses.
        const double record_start = segment.init_tdb_s + static_cast<double>(i) * segment.interval_s;
        const double used_start = std::fmax(record_start, segment.start_tdb_s);
        const double used_end = std::fmin(record_start + segment.interval_s, segment.end_tdb_s);
        if (!(radius > 0.0) || used_start < mid - radius - k_record_slack_s ||
            used_end > mid + radius + k_record_slack_s) {
            return make_error(ErrorCode::data_format,
                              what + ": record " + std::to_string(i) + " does not span its interval");
        }
    }
    return ChebyshevSegment(segment);
}

std::size_t ChebyshevSegment::record_index(time::Instant t) const noexcept {
    const double index = std::floor(t.seconds_since(init_) / segment_->interval_s);
    if (!(index > 0.0)) {
        return 0; // also NaN
    }
    const auto last = static_cast<std::size_t>(segment_->record_count - 1u);
    return index >= static_cast<double>(last) ? last : static_cast<std::size_t>(index);
}

std::optional<frames::StateVector> ChebyshevSegment::state(time::Instant t) const noexcept {
    if (!covers(t)) {
        return std::nullopt; // also rejects invalid instants: NaN compares false
    }
    const double* record = segment_->records.data() + record_index(t) * segment_->record_size;
    const double mid = record[0];
    const double radius = record[1];
    // t - MID from the two parts of t: days * 86400 is exact, and its difference with MID is exact while
    // both are close (Sterbenz), so the only rounding is the final addition. With t made from one double
    // of TDB seconds, this equals SPICE's (ET - MID).
    const double dt_s =
        (static_cast<double>(t.days_since_j2000()) * constants::k_seconds_per_day - mid) + t.seconds_of_day();
    const double s = dt_s / radius;
    const std::size_t n = coefficients_;
    const double* coefficients = record + 2;

    double position[3];
    double velocity[3];
    for (std::size_t axis = 0; axis < 3; ++axis) {
        const std::span<const double> c(coefficients + axis * n, n);
        if (segment_->spk_type == 2) {
            const math::ChebyshevValue v = math::chebyshev_with_derivative(c, s);
            position[axis] = v.value;
            velocity[axis] = v.derivative / radius;
        } else {
            position[axis] = math::chebyshev(c, s);
            velocity[axis] = math::chebyshev(std::span<const double>(coefficients + (3 + axis) * n, n), s);
        }
    }
    return frames::StateVector{{position[0], position[1], position[2]},
                               {velocity[0], velocity[1], velocity[2]}};
}

} // namespace usim::ephemeris
