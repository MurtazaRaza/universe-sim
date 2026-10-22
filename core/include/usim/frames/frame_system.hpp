#pragma once

#include "usim/frames/frame_id.hpp"
#include "usim/frames/frame_transform.hpp"
#include "usim/frames/iau_rotation.hpp"
#include "usim/math/mat3.hpp"
#include "usim/result.hpp"
#include "usim/time/time_point.hpp"

#include <optional>
#include <vector>

namespace usim::frames {

/// The constant rotation from ICRF to ECLIPJ2000: a rotation of the frame by the J2000 obliquity
/// (constants::k_eclipj2000_obliquity_rad) about the shared X axis (the J2000 equinox). Matches
/// SPICE pxform("J2000", "ECLIPJ2000").
[[nodiscard]] math::Mat3 icrf_to_eclipj2000() noexcept;

/// Resolves transforms between frames (SPEC FR-SIM-09).
///
/// The inertial frames (ICRF and ECLIPJ2000) are built in. Body-fixed frames come from IAU rotation
/// elements (usually the data pack's): the frame of a body without elements is not found.
class FrameSystem {
public:
    /// A frame system with the built-in inertial frames only.
    FrameSystem() = default;

    /// A frame system with the inertial frames and the IAU body-fixed frame of each body in `rotations`.
    /// Where a body appears twice, the first elements are used.
    explicit FrameSystem(std::vector<IauRotationElements> rotations);

    /// The rotation elements of `body`, or nullptr.
    [[nodiscard]] const IauRotationElements* find_rotation(catalog::BodyId body) const noexcept;

    /// The transform that takes states in `from` to states in `to` at instant `t` (TDB). Fails with
    /// ErrorCode::not_found for a body-fixed frame without rotation elements.
    [[nodiscard]] Result<FrameTransform> transform(FrameId from, FrameId to, time::Instant t) const;

    /// The same transform without allocating; nothing if a frame is not found.
    [[nodiscard]] std::optional<FrameTransform> try_transform(FrameId from, FrameId to, time::Instant t) const noexcept;

private:
    // The transform from the ICRF to `frame` at `t`, or nothing if the frame is not found.
    [[nodiscard]] std::optional<FrameTransform> from_icrf(FrameId frame, time::Instant t) const noexcept;

    std::vector<IauRotationElements> rotations_; // sorted by body
};

} // namespace usim::frames
