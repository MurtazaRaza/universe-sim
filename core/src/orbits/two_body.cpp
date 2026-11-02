#include "usim/orbits/two_body.hpp"

#include "usim/math/vec3.hpp"

#include <cmath>
#include <numbers>

namespace usim::orbits {

namespace {

bool positive_finite(double x) noexcept {
    return std::isfinite(x) && x > 0.0;
}

} // namespace

Result<double> semi_major_axis_km(const frames::StateVector& state, double gm_km3_s2) {
    if (!positive_finite(gm_km3_s2)) {
        return make_error(ErrorCode::invalid_argument, "GM must be positive and finite");
    }
    const double r = math::norm(state.position_km);
    const double v2 = math::norm_squared(state.velocity_km_s);
    if (!positive_finite(r) || !std::isfinite(v2)) {
        return make_error(ErrorCode::invalid_argument, "the relative position must be finite and not zero");
    }
    // Twice the negative specific orbital energy divided by GM: positive exactly when the orbit is bound.
    const double inverse_a = 2.0 / r - v2 / gm_km3_s2;
    if (!(inverse_a > 0.0)) {
        return make_error(ErrorCode::unsupported, "the orbit is not elliptic (parabolic or hyperbolic)");
    }
    return 1.0 / inverse_a;
}

double orbital_period_s(double semi_major_axis_km, double gm_km3_s2) noexcept {
    return 2.0 * std::numbers::pi *
           std::sqrt(semi_major_axis_km * semi_major_axis_km * semi_major_axis_km / gm_km3_s2);
}

Result<HohmannTransfer> hohmann_transfer(double gm_km3_s2, double r1_km, double r2_km) {
    if (!positive_finite(gm_km3_s2) || !positive_finite(r1_km) || !positive_finite(r2_km)) {
        return make_error(ErrorCode::invalid_argument, "GM and both radii must be positive and finite");
    }
    const double sum = r1_km + r2_km;
    const double a = 0.5 * sum;
    HohmannTransfer transfer;
    transfer.transfer_time_s = std::numbers::pi * std::sqrt(a * a * a / gm_km3_s2);
    transfer.departure_delta_v_km_s =
        std::fabs(std::sqrt(gm_km3_s2 / r1_km) * (std::sqrt(2.0 * r2_km / sum) - 1.0));
    transfer.arrival_delta_v_km_s =
        std::fabs(std::sqrt(gm_km3_s2 / r2_km) * (1.0 - std::sqrt(2.0 * r1_km / sum)));
    return transfer;
}

} // namespace usim::orbits
