#pragma once

#include "usim/catalog/body_id.hpp"
#include "usim/catalog/quantity.hpp"
#include "usim/frames/frame_id.hpp"

#include <cstdint>
#include <vector>

namespace usim::frames {

/// IAU rotation elements of one body, as published in a SPICE text PCK (pck00011.tpc) and carried by the
/// data pack in the PCK's own units. Evaluating them is P4-03; this is the data only.
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

} // namespace usim::frames
