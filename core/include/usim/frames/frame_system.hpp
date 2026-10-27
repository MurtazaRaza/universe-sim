#pragma once

#include "usim/frames/frame_id.hpp"
#include "usim/frames/frame_transform.hpp"
#include "usim/frames/iau_rotation.hpp"
#include "usim/frames/pck_orientation.hpp"
#include "usim/math/mat3.hpp"
#include "usim/result.hpp"
#include "usim/time/time_point.hpp"

#include <optional>
#include <span>
#include <vector>

namespace usim::frames {

/// The constant rotation from ICRF to ECLIPJ2000: a rotation of the frame by the J2000 obliquity
/// (constants::k_eclipj2000_obliquity_rad) about the shared X axis (the J2000 equinox). Matches
/// SPICE pxform("J2000", "ECLIPJ2000").
[[nodiscard]] math::Mat3 icrf_to_eclipj2000() noexcept;

/// Resolves transforms between frames (SPEC FR-SIM-09).
///
/// The inertial frames (ICRF and ECLIPJ2000) are built in. A body's body-fixed frame comes from its binary
/// PCK orientation model where it has one (P6-05; the Moon: MOON_ME_DE440_ME421), and otherwise from its IAU
/// rotation elements (usually the data pack's). The frame of a body with neither is not found; a binary PCK
/// frame is available only where its segments cover the instant (no fallback to the IAU elements).
class FrameSystem {
public:
    /// A frame system with the built-in inertial frames only.
    FrameSystem() = default;

    /// A frame system with the inertial frames and the body-fixed frame of each body in `rotations` and
    /// `orientations`. Where a body appears twice in one of them, the first entry is used. Non-owning for
    /// `orientations`: the models (usually the data pack's) must outlive the frame system.
    explicit FrameSystem(std::vector<IauRotationElements> rotations, std::span<const PckOrientation> orientations = {});

    /// The IAU rotation elements of `body`, or nullptr.
    [[nodiscard]] const IauRotationElements* find_rotation(catalog::BodyId body) const noexcept;

    /// The binary PCK orientation model of `body`, or nullptr.
    [[nodiscard]] const PckOrientation* find_pck_orientation(catalog::BodyId body) const noexcept;

    /// True if `body` has a body-fixed frame (a binary PCK model or IAU rotation elements).
    [[nodiscard]] bool has_body_fixed_frame(catalog::BodyId body) const noexcept;

    /// The transform that takes states in `from` to states in `to` at instant `t` (TDB). Fails with
    /// ErrorCode::not_found for a body-fixed frame without a rotation model, and ErrorCode::out_of_coverage
    /// for a binary PCK frame whose segments do not cover `t`.
    [[nodiscard]] Result<FrameTransform> transform(FrameId from, FrameId to, time::Instant t) const;

    /// The same transform without allocating; nothing if a frame is not found.
    [[nodiscard]] std::optional<FrameTransform> try_transform(FrameId from, FrameId to, time::Instant t) const noexcept;

private:
    // The transform from the ICRF to `frame` at `t`, or nothing if the frame is not found.
    [[nodiscard]] std::optional<FrameTransform> from_icrf(FrameId frame, time::Instant t) const noexcept;

    // A binary PCK model with its constant offset, computed once.
    struct PckModel {
        const PckOrientation* model;
        math::Mat3 offset;
    };

    std::vector<IauRotationElements> rotations_; // sorted by body
    std::vector<PckModel> orientations_;         // sorted by body
};

} // namespace usim::frames
