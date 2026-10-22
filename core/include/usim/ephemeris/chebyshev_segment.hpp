#pragma once

#include "usim/data/data_pack.hpp"
#include "usim/ephemeris/coverage.hpp"
#include "usim/frames/frame_transform.hpp"
#include "usim/result.hpp"
#include "usim/time/time_point.hpp"

#include <cstddef>
#include <cstdint>
#include <optional>

namespace usim::ephemeris {

/// Evaluates one SPK type 2 or type 3 segment from a data pack (NAIF SPK Required Reading; the record
/// layout is in docs/formats/DATA_PACK.md, section EPHS).
///
/// A non-owning view: the data::EphemerisSegment, usually held by a data::DataPack, must outlive it.
/// Evaluation is noexcept and allocates nothing.
class ChebyshevSegment {
public:
    /// Checks the records and makes the view. Fails with ErrorCode::data_format if a record's RADIUS is
    /// not positive or its interval [MID - RADIUS, MID + RADIUS] does not contain the instants the record
    /// is used for (beyond a tolerance of one microsecond).
    [[nodiscard]] static Result<ChebyshevSegment> create(const data::EphemerisSegment& segment);

    /// The segment's data.
    [[nodiscard]] const data::EphemerisSegment& data() const noexcept { return *segment_; }

    /// The body whose state the segment gives.
    [[nodiscard]] catalog::BodyId target() const noexcept { return segment_->target; }

    /// The body the state is relative to.
    [[nodiscard]] catalog::BodyId center() const noexcept { return segment_->center; }

    /// The frame of the states (an inertial frame).
    [[nodiscard]] frames::FrameId frame() const noexcept { return segment_->frame; }

    /// The segment's coverage, inclusive at both ends (TDB).
    [[nodiscard]] TimeInterval coverage() const noexcept { return coverage_; }

    /// True if the segment covers `t`.
    [[nodiscard]] bool covers(time::Instant t) const noexcept { return coverage_.contains(t); }

    /// Index of the record used at `t`: floor((t - init) / interval), clamped to the records, as in
    /// SPICE (the final instant of coverage uses the last record).
    [[nodiscard]] std::size_t record_index(time::Instant t) const noexcept;

    /// The state of target() relative to center() in frame() at `t` (TDB): position in km and velocity in
    /// km/s. Type 2 velocities are the derivative of the position polynomials. Nothing if `t` is outside
    /// the coverage or invalid; the segment never extrapolates (SPEC FR-SIM-03).
    [[nodiscard]] std::optional<frames::StateVector> state(time::Instant t) const noexcept;

private:
    explicit ChebyshevSegment(const data::EphemerisSegment& segment) noexcept;

    const data::EphemerisSegment* segment_;
    TimeInterval coverage_;
    time::Instant init_;
    std::size_t coefficients_; // per component: degree + 1
};

} // namespace usim::ephemeris
