#pragma once

#include "usim/frames/frame_transform.hpp"
#include "usim/result.hpp"

namespace usim::orbits {

/// Osculating semi-major axis, km, of the two-body orbit with the relative state `state` (position km,
/// velocity km/s, any inertial frame) around a body of gravitational parameter `gm_km3_s2`, from the vis-viva
/// equation a = 1 / (2 / r - v^2 / GM). For the relative orbit of two bodies, GM is the sum of both. Fails
/// with ErrorCode::invalid_argument for a non-positive or non-finite GM or a zero or non-finite position, and
/// with ErrorCode::unsupported when the orbit is not elliptic (parabolic or hyperbolic).
[[nodiscard]] Result<double> semi_major_axis_km(const frames::StateVector& state, double gm_km3_s2);

/// Period, s, of an elliptic two-body orbit with semi-major axis `semi_major_axis_km` around GM `gm_km3_s2`:
/// 2 pi sqrt(a^3 / GM). Precondition: both positive.
[[nodiscard]] double orbital_period_s(double semi_major_axis_km, double gm_km3_s2) noexcept;

/// A Hohmann transfer: the half ellipse that touches a circular orbit of radius r1 at its periapsis or
/// apoapsis and a coplanar circular orbit of radius r2 at the other apse (Hohmann 1925; Vallado,
/// "Fundamentals of Astrodynamics and Applications", section 6.3). Two-body motion around one central body;
/// the spacecraft's mass is negligible.
struct HohmannTransfer {
    double transfer_time_s = 0.0;        ///< Half the period of the transfer ellipse.
    double departure_delta_v_km_s = 0.0; ///< Speed change from the circular orbit at r1 onto the ellipse.
    double arrival_delta_v_km_s = 0.0;   ///< Speed change from the ellipse onto the circular orbit at r2.
};

/// The Hohmann transfer between circular orbits of radii `r1_km` and `r2_km` around a body of GM `gm_km3_s2`:
/// time pi sqrt(((r1 + r2) / 2)^3 / GM), and speed changes |sqrt(GM / r1) (sqrt(2 r2 / (r1 + r2)) - 1)| and
/// |sqrt(GM / r2) (1 - sqrt(2 r1 / (r1 + r2)))|. Equal radii give zero speed changes and half a circular
/// period. Fails with ErrorCode::invalid_argument unless all three are positive and finite.
[[nodiscard]] Result<HohmannTransfer> hohmann_transfer(double gm_km3_s2, double r1_km, double r2_km);

} // namespace usim::orbits
