#pragma once

#include "usim/frames/frame_id.hpp"
#include "usim/frames/frame_transform.hpp"
#include "usim/math/mat3.hpp"
#include "usim/result.hpp"
#include "usim/time/time_point.hpp"

namespace usim::frames {

/// The constant rotation from ICRF to ECLIPJ2000: a rotation of the frame by the J2000 obliquity
/// (constants::k_eclipj2000_obliquity_rad) about the shared X axis (the J2000 equinox). Matches
/// SPICE pxform("J2000", "ECLIPJ2000").
[[nodiscard]] math::Mat3 icrf_to_eclipj2000() noexcept;

/// Resolves transforms between frames (SPEC FR-SIM-09).
///
/// The inertial frames (ICRF and ECLIPJ2000) are built in. Body-fixed frames need the IAU rotation models
/// from the data pack and arrive in P4-03; until then requests involving them fail with
/// ErrorCode::unsupported.
class FrameSystem {
public:
    /// A frame system with the built-in inertial frames.
    FrameSystem() = default;

    /// The transform that takes states in `from` to states in `to` at instant `t` (TDB).
    [[nodiscard]] Result<FrameTransform> transform(FrameId from, FrameId to, time::Instant t) const;

private:
    // The transform from the ICRF to `frame` at `t`.
    [[nodiscard]] Result<FrameTransform> from_icrf(FrameId frame, time::Instant t) const;
};

} // namespace usim::frames
