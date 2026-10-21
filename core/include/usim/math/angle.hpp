#pragma once

#include <cmath>
#include <numbers>

namespace usim::math {

/// pi.
inline constexpr double k_pi = std::numbers::pi;

/// 2 pi (one full turn in radians).
inline constexpr double k_two_pi = 2.0 * std::numbers::pi;

/// pi / 2.
inline constexpr double k_half_pi = 0.5 * std::numbers::pi;

/// Radians per degree.
inline constexpr double k_rad_per_deg = std::numbers::pi / 180.0;

/// Radians per arcsecond.
inline constexpr double k_rad_per_arcsec = std::numbers::pi / 648'000.0;

/// Converts degrees to radians.
[[nodiscard]] constexpr double deg_to_rad(double degrees) noexcept {
    return degrees * k_rad_per_deg;
}

/// Converts radians to degrees.
[[nodiscard]] constexpr double rad_to_deg(double radians) noexcept {
    return radians / k_rad_per_deg;
}

/// Converts arcseconds to radians.
[[nodiscard]] constexpr double arcsec_to_rad(double arcseconds) noexcept {
    return arcseconds * k_rad_per_arcsec;
}

/// Wraps an angle in radians into [0, 2 pi). Non-finite input gives NaN.
[[nodiscard]] inline double wrap_two_pi(double angle_rad) noexcept {
    double r = std::fmod(angle_rad, k_two_pi); // exact, in (-2 pi, 2 pi)
    if (r < 0.0) {
        r += k_two_pi;
    }
    // A tiny negative r can round up to exactly 2 pi.
    return r >= k_two_pi ? 0.0 : r;
}

/// Wraps an angle in radians into [-pi, pi). Non-finite input gives NaN.
[[nodiscard]] inline double wrap_pi(double angle_rad) noexcept {
    const double r = wrap_two_pi(angle_rad);
    return r >= k_pi ? r - k_two_pi : r;
}

} // namespace usim::math
