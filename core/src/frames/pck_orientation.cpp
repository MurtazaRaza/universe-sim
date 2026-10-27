#include "usim/frames/pck_orientation.hpp"

#include "usim/constants.hpp"
#include "usim/frames/euler.hpp"
#include "usim/frames/frame_system.hpp"
#include "usim/math/angle.hpp"
#include "usim/math/chebyshev.hpp"

#include <cmath>
#include <cstddef>
#include <span>

namespace usim::frames {

namespace {

// A record may be used up to this far outside [MID - RADIUS, MID + RADIUS], as for SPK segments
// (ephemeris::ChebyshevSegment): record boundaries computed in floating point do not land exactly on the
// boundaries of the record intervals.
constexpr double k_record_slack_s = 1.0e-6;

constexpr std::size_t k_angles = 3;

bool covers(const PckSegment& segment, time::Instant t) noexcept {
    // Comparing through Instants, as ephemeris coverage does; NaN compares false.
    return time::Instant::from_seconds_since_j2000(segment.start_tdb_s) <= t &&
           t <= time::Instant::from_seconds_since_j2000(segment.end_tdb_s);
}

double radians_per_unit(AngleUnit unit) noexcept {
    switch (unit) {
    case AngleUnit::degrees:
        return math::deg_to_rad(1.0);
    case AngleUnit::arcseconds:
        return math::k_rad_per_arcsec;
    case AngleUnit::radians:
        break;
    }
    return 1.0;
}

// The Euler angles and their rates at t from the segment's record for t (see ChebyshevSegment::state, which
// evaluates SPK type 2 records the same way).
Euler313 evaluate(const PckSegment& segment, time::Instant t) noexcept {
    const time::Instant init = time::Instant::from_seconds_since_j2000(segment.init_tdb_s);
    const double index = std::floor(t.seconds_since(init) / segment.interval_s);
    const auto last = static_cast<std::size_t>(segment.record_count - 1u);
    const std::size_t i = !(index > 0.0)                         ? 0
                          : index >= static_cast<double>(last) ? last
                                                               : static_cast<std::size_t>(index);
    const double* record = segment.records.data() + i * segment.record_size;
    const double mid = record[0];
    const double radius = record[1];
    const double dt_s =
        (static_cast<double>(t.days_since_j2000()) * constants::k_seconds_per_day - mid) + t.seconds_of_day();
    const double s = dt_s / radius;
    const std::size_t n = segment.degree() + 1u;
    double angle[k_angles];
    double rate[k_angles];
    for (std::size_t k = 0; k < k_angles; ++k) {
        const std::span<const double> coefficients(record + 2 + k * n, n);
        const math::ChebyshevValue v = math::chebyshev_with_derivative(coefficients, s);
        angle[k] = v.value;
        rate[k] = v.derivative / radius;
    }
    return {angle[0], angle[1], angle[2], rate[0], rate[1], rate[2]};
}

} // namespace

Result<void> check_pck_orientation(const PckOrientation& model) {
    const std::string what =
        "orientation of " + catalog::to_string(model.body) + " (" + model.frame_name + ")";
    if (model.segments.empty()) {
        return make_error(ErrorCode::data_format, what + ": no segments");
    }
    for (std::size_t k = 0; k < k_angles; ++k) {
        if (!std::isfinite(model.offset_angles[k]) || model.offset_axes[k] < 1 || model.offset_axes[k] > 3) {
            return make_error(ErrorCode::data_format,
                              what + ": offset angles must be finite, about axes 1 to 3");
        }
    }
    if (!model.offset_source &&
        (model.frame_name != model.pck_frame_name || model.offset_angles != std::array<double, 3>{} ||
         model.offset_axes != std::array<std::int32_t, 3>{1, 2, 3} || model.offset_unit != AngleUnit::radians)) {
        return make_error(ErrorCode::data_format, what + ": without an offset, the frame must be the PCK frame, "
                                                         "with zero angles about the axes 1, 2, 3 in radians");
    }
    for (const PckSegment& segment : model.segments) {
        const std::string where = what + ", " + segment.source.locator;
        if (segment.base_frame != FrameKind::icrf && segment.base_frame != FrameKind::eclipj2000) {
            return make_error(ErrorCode::data_format, where + ": base frame must be the ICRF or ECLIPJ2000");
        }
        const std::size_t size = segment.record_size;
        if (size < 2 + k_angles || (size - 2) % k_angles != 0 || segment.record_count == 0 ||
            segment.records.size() != size * segment.record_count) {
            return make_error(ErrorCode::data_format, where + ": records do not match the segment's layout");
        }
        const double records_end =
            segment.init_tdb_s + static_cast<double>(segment.record_count) * segment.interval_s;
        // The coverage may end up to the slack past the last record: see DATA_PACK.md, section ORNT (the
        // pipeline's trimming recomputes init, so the grid can end an ulp before the PCK file's coverage).
        if (!(segment.interval_s > 0.0) || !(segment.init_tdb_s <= segment.start_tdb_s) ||
            !(segment.start_tdb_s < segment.end_tdb_s) || !(segment.end_tdb_s <= records_end + k_record_slack_s)) {
            return make_error(ErrorCode::data_format, where + ": invalid coverage");
        }
        for (std::size_t i = 0; i < segment.record_count; ++i) {
            const double mid = segment.records[i * size];
            const double radius = segment.records[i * size + 1];
            const double record_start = segment.init_tdb_s + static_cast<double>(i) * segment.interval_s;
            const double used_start = std::fmax(record_start, segment.start_tdb_s);
            const double used_end = std::fmin(record_start + segment.interval_s, segment.end_tdb_s);
            if (!(radius > 0.0) || used_start < mid - radius - k_record_slack_s ||
                used_end > mid + radius + k_record_slack_s) {
                return make_error(ErrorCode::data_format,
                                  where + ": record " + std::to_string(i) + " does not span its interval");
            }
        }
        for (const double value : segment.records) {
            if (!std::isfinite(value)) {
                return make_error(ErrorCode::data_format, where + ": non-finite coefficient");
            }
        }
    }
    return {};
}

math::Mat3 pck_offset(const PckOrientation& model) noexcept {
    if (!model.offset_source) {
        return math::Mat3::identity();
    }
    const double unit = radians_per_unit(model.offset_unit);
    const math::Mat3 to_pck_frame = frame_rotation(model.offset_angles[0] * unit, model.offset_axes[0]) *
                                    frame_rotation(model.offset_angles[1] * unit, model.offset_axes[1]) *
                                    frame_rotation(model.offset_angles[2] * unit, model.offset_axes[2]);
    return math::transpose(to_pck_frame);
}

bool pck_covers(const PckOrientation& model, time::Instant t) noexcept {
    for (const PckSegment& segment : model.segments) {
        if (covers(segment, t)) {
            return true;
        }
    }
    return false;
}

std::optional<FrameTransform> pck_frame_transform(const PckOrientation& model, time::Instant t) noexcept {
    // Later segments take precedence, as in SPICE.
    for (auto it = model.segments.rbegin(); it != model.segments.rend(); ++it) {
        if (covers(*it, t)) {
            const FrameTransform from_base = euler_313_transform(evaluate(*it, t));
            if (it->base_frame == FrameKind::eclipj2000) {
                return FrameTransform::inertial(icrf_to_eclipj2000()).then(from_base);
            }
            return from_base;
        }
    }
    return std::nullopt;
}

std::optional<FrameTransform> pck_transform(const PckOrientation& model, time::Instant t) noexcept {
    std::optional<FrameTransform> to_pck_frame = pck_frame_transform(model, t);
    if (!to_pck_frame || !model.offset_source) {
        return to_pck_frame;
    }
    return to_pck_frame->then(FrameTransform::inertial(pck_offset(model)));
}

} // namespace usim::frames
