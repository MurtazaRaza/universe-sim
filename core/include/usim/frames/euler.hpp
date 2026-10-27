#pragma once

#include "usim/frames/frame_transform.hpp"
#include "usim/math/mat3.hpp"

#include <cstdint>

namespace usim::frames {

/// The frame rotation [angle]_axis (SPICE ROTATE(angle, axis)): it takes coordinates into a frame rotated by
/// `angle_rad` about `axis` (1 = X, 2 = Y, 3 = Z). Equals Mat3::rotation_x(-angle) and so on. Any other axis
/// gives the identity.
[[nodiscard]] math::Mat3 frame_rotation(double angle_rad, std::int32_t axis) noexcept;

/// Three Euler angles in the 3-1-3 sequence of IAU rotation models and binary PCK type 2 segments (PCK
/// Required Reading), with their rates.
struct Euler313 {
    double phi_rad = 0.0;          ///< First rotation, about Z (for IAU models: pi/2 + alpha_0).
    double delta_rad = 0.0;        ///< Second rotation, about X (for IAU models: pi/2 - delta_0).
    double w_rad = 0.0;            ///< Third rotation, about Z (the prime meridian angle W).
    double phi_rate_rad_s = 0.0;   ///< d phi / dt, rad per TDB second.
    double delta_rate_rad_s = 0.0; ///< d delta / dt, rad per TDB second.
    double w_rate_rad_s = 0.0;     ///< dW / dt, rad per TDB second.
};

/// The transform R = [w]_3 [delta]_1 [phi]_3 from the base frame to the rotated frame, with dR/dt from the
/// angle rates.
[[nodiscard]] FrameTransform euler_313_transform(const Euler313& angles) noexcept;

} // namespace usim::frames
