#include "usim/frames/iau_rotation.hpp"

#include "usim/constants.hpp"
#include "usim/frames/frame_system.hpp"
#include "usim/math/angle.hpp"

#include <cmath>
#include <cstddef>
#include <span>

namespace usim::frames {

namespace {

constexpr double k_seconds_per_century = constants::k_days_per_julian_century * constants::k_seconds_per_day;

// c0 + c1 x + c2 x^2 for the coefficients present, and its derivative with respect to x.
struct PolyValue {
    double value;
    double derivative;
};

PolyValue polynomial(std::span<const double> c, double x) noexcept {
    double value = 0.0;
    double derivative = 0.0;
    // Horner from the highest degree.
    for (std::size_t k = c.size(); k-- > 0;) {
        derivative = derivative * x + value;
        value = value * x + c[k];
    }
    return {value, derivative};
}

// The frame rotation [angle]_3 (SPICE ROTATE(angle, 3)) and its derivative with respect to the angle.
math::Mat3 frame_z(double c, double s) noexcept {
    return math::Mat3{{c, s, 0.0, -s, c, 0.0, 0.0, 0.0, 1.0}};
}
math::Mat3 frame_z_derivative(double c, double s) noexcept {
    return math::Mat3{{-s, c, 0.0, -c, -s, 0.0, 0.0, 0.0, 0.0}};
}

// The frame rotation [angle]_1 and its derivative with respect to the angle.
math::Mat3 frame_x(double c, double s) noexcept {
    return math::Mat3{{1.0, 0.0, 0.0, 0.0, c, s, 0.0, -s, c}};
}
math::Mat3 frame_x_derivative(double c, double s) noexcept {
    return math::Mat3{{0.0, 0.0, 0.0, 0.0, -s, c, 0.0, -c, -s}};
}

} // namespace

IauAngles iau_angles(const IauRotationElements& e, time::Instant t) noexcept {
    // TDB days and Julian centuries past J2000. The two parts of t keep the days exact to the resolution
    // of one double of days.
    const double days =
        static_cast<double>(t.days_since_j2000()) + t.seconds_of_day() / constants::k_seconds_per_day;
    const double centuries = days / constants::k_days_per_julian_century;

    const PolyValue ra = polynomial(e.pole_ra_deg, centuries);   // deg, deg/century
    const PolyValue dec = polynomial(e.pole_dec_deg, centuries); // deg, deg/century
    const PolyValue w = polynomial(e.pm_deg, days);              // deg, deg/day
    double ra_deg = ra.value;
    double dec_deg = dec.value;
    double w_deg = w.value;
    double ra_rate = ra.derivative / k_seconds_per_century; // deg/s
    double dec_rate = dec.derivative / k_seconds_per_century;
    double w_rate = w.derivative / constants::k_seconds_per_day;

    // Nutation and precession terms. Each phase angle theta_j is a polynomial in centuries (degrees).
    const auto per_angle = static_cast<std::size_t>(e.phase_degree + 1);
    const std::size_t angles = e.angle_count();
    for (std::size_t j = 0; j < angles; ++j) {
        const PolyValue theta = polynomial(
            std::span<const double>(e.nut_prec_angles_deg).subspan(j * per_angle, per_angle), centuries);
        const double theta_rad = math::deg_to_rad(theta.value);
        const double theta_rate_rad_s = math::deg_to_rad(theta.derivative / k_seconds_per_century);
        const double s = std::sin(theta_rad);
        const double c = std::cos(theta_rad);
        if (j < e.nut_prec_ra_deg.size()) {
            ra_deg += e.nut_prec_ra_deg[j] * s;
            ra_rate += e.nut_prec_ra_deg[j] * c * theta_rate_rad_s;
        }
        if (j < e.nut_prec_dec_deg.size()) {
            dec_deg += e.nut_prec_dec_deg[j] * c;
            dec_rate -= e.nut_prec_dec_deg[j] * s * theta_rate_rad_s;
        }
        if (j < e.nut_prec_pm_deg.size()) {
            w_deg += e.nut_prec_pm_deg[j] * s;
            w_rate += e.nut_prec_pm_deg[j] * c * theta_rate_rad_s;
        }
    }
    return {math::deg_to_rad(ra_deg),  math::deg_to_rad(dec_deg),  math::deg_to_rad(w_deg),
            math::deg_to_rad(ra_rate), math::deg_to_rad(dec_rate), math::deg_to_rad(w_rate)};
}

FrameTransform iau_transform(const IauRotationElements& e, time::Instant t) noexcept {
    const IauAngles a = iau_angles(e, t);
    // R = M_w M_dec M_ra with M_w = [W]_3, M_dec = [pi/2 - dec]_1 and M_ra = [pi/2 + ra]_3, so
    // dR/dt = M_w' W' M_dec M_ra + M_w M_dec' (-dec') M_ra + M_w M_dec M_ra' ra'.
    const double phi = math::k_half_pi - a.pole_dec_rad;
    const double psi = math::k_half_pi + a.pole_ra_rad;
    const double cw = std::cos(a.prime_meridian_rad);
    const double sw = std::sin(a.prime_meridian_rad);
    const double cp = std::cos(phi);
    const double sp = std::sin(phi);
    const double cs = std::cos(psi);
    const double ss = std::sin(psi);
    const math::Mat3 m_w = frame_z(cw, sw);
    const math::Mat3 m_dec = frame_x(cp, sp);
    const math::Mat3 m_ra = frame_z(cs, ss);
    const math::Mat3 pole = m_dec * m_ra;
    math::Mat3 rotation = m_w * pole;
    math::Mat3 rate = frame_z_derivative(cw, sw) * pole * a.prime_meridian_rate_rad_s +
                      m_w * frame_x_derivative(cp, sp) * m_ra * (-a.pole_dec_rate_rad_s) +
                      m_w * m_dec * frame_z_derivative(cs, ss) * a.pole_ra_rate_rad_s;
    if (e.frame.kind() == FrameKind::eclipj2000) {
        const math::Mat3 to_ecliptic = icrf_to_eclipj2000();
        rotation = rotation * to_ecliptic;
        rate = rate * to_ecliptic;
    }
    return {rotation, rate};
}

} // namespace usim::frames
