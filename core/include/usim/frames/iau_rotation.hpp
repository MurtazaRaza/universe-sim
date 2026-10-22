#pragma once

#include "usim/catalog/body_id.hpp"
#include "usim/catalog/quantity.hpp"
#include "usim/frames/frame_id.hpp"
#include "usim/frames/frame_transform.hpp"
#include "usim/time/time_point.hpp"

#include <cstdint>
#include <vector>

namespace usim::frames {

/// IAU rotation elements of one body, as published in a SPICE text PCK (pck00011.tpc) and carried by the
/// data pack in the PCK's own units. iau_angles() and iau_transform() evaluate them.
///
/// The model (PCK Required Reading; Archinal et al. 2018), with T in Julian centuries (36525 days) of TDB
/// since J2000 and d in days of TDB since J2000, all angles in degrees:
///
///     theta_j = sum_k nut_prec_angles_deg[j * (phase_degree + 1) + k] * T^k
///     alpha_0 = pole_ra_deg[0] + pole_ra_deg[1] T + pole_ra_deg[2] T^2 + sum_j nut_prec_ra_deg[j] sin(theta_j)
///     delta_0 = pole_dec_deg[0] + pole_dec_deg[1] T + pole_dec_deg[2] T^2 + sum_j nut_prec_dec_deg[j] cos(theta_j)
///     W       = pm_deg[0] + pm_deg[1] d + pm_deg[2] d^2 + sum_j nut_prec_pm_deg[j] sin(theta_j)
///
/// Polynomial coefficients not given are zero. The nutation arrays may be shorter than the number of
/// phase angles; missing terms are zero.
struct IauRotationElements {
    catalog::BodyId body;               ///< The body these elements orient.
    FrameId frame = FrameId::icrf();    ///< Frame of the pole coordinates (the ICRF for pck00011.tpc).
    std::vector<double> pole_ra_deg;    ///< alpha_0 polynomial: deg, deg/century, deg/century^2 (1 to 3 values).
    std::vector<double> pole_dec_deg;   ///< delta_0 polynomial: deg, deg/century, deg/century^2 (1 to 3 values).
    std::vector<double> pm_deg;         ///< W polynomial: deg, deg/day, deg/day^2 (1 to 3 values).
    std::vector<double> nut_prec_ra_deg;  ///< Sine coefficients of alpha_0, deg.
    std::vector<double> nut_prec_dec_deg; ///< Cosine coefficients of delta_0, deg.
    std::vector<double> nut_prec_pm_deg;  ///< Sine coefficients of W, deg.
    /// The system whose phase angles (BODY<n>_NUT_PREC_ANGLES) apply, for example the Earth-Moon barycenter
    /// for the Moon. Invalid when there are no nutation terms.
    catalog::BodyId angles_body;
    std::int32_t phase_degree = 1;      ///< Degree of the phase angle polynomials (BODY<n>_MAX_PHASE_DEGREE).
    /// Phase angle polynomials, phase_degree + 1 coefficients per angle: deg, deg/century, deg/century^2, ...
    std::vector<double> nut_prec_angles_deg;
    catalog::SourceRef source;          ///< The PCK and the kernel variables these values come from.

    /// Number of phase angles.
    [[nodiscard]] std::size_t angle_count() const noexcept {
        return nut_prec_angles_deg.size() / static_cast<std::size_t>(phase_degree + 1);
    }
};

/// The pole and prime meridian of a body at one instant, with their rates, from its IAU elements.
struct IauAngles {
    double pole_ra_rad = 0.0;              ///< alpha_0: right ascension of the north pole, rad.
    double pole_dec_rad = 0.0;             ///< delta_0: declination of the north pole, rad.
    double prime_meridian_rad = 0.0;       ///< W: prime meridian angle, rad (not reduced to one turn).
    double pole_ra_rate_rad_s = 0.0;       ///< d alpha_0 / dt, rad per TDB second.
    double pole_dec_rate_rad_s = 0.0;      ///< d delta_0 / dt, rad per TDB second.
    double prime_meridian_rate_rad_s = 0.0; ///< dW / dt, rad per TDB second.
};

/// Evaluates the model in the IauRotationElements documentation at `t` (TDB), with the rates as its
/// analytic time derivatives.
[[nodiscard]] IauAngles iau_angles(const IauRotationElements& elements, time::Instant t) noexcept;

/// The transform from the ICRF to the body's IAU body-fixed frame at `t` (TDB), with its rotation rate:
/// R = [W]_3 [pi/2 - delta_0]_1 [pi/2 + alpha_0]_3 with [x]_i the frame rotation by x about axis i (PCK
/// Required Reading). Equals SPICE sxform("J2000", "IAU_<body>", t). Elements given relative to
/// ECLIPJ2000 are composed with the ICRF to ECLIPJ2000 rotation.
[[nodiscard]] FrameTransform iau_transform(const IauRotationElements& elements, time::Instant t) noexcept;

} // namespace usim::frames
