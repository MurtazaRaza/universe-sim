#include "usim/frames/euler.hpp"

#include <cmath>

namespace usim::frames {

namespace {

// The frame rotation [angle]_3 and its derivative with respect to the angle.
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

// The frame rotation [angle]_2.
math::Mat3 frame_y(double c, double s) noexcept {
    return math::Mat3{{c, 0.0, -s, 0.0, 1.0, 0.0, s, 0.0, c}};
}

} // namespace

math::Mat3 frame_rotation(double angle_rad, std::int32_t axis) noexcept {
    const double c = std::cos(angle_rad);
    const double s = std::sin(angle_rad);
    switch (axis) {
    case 1:
        return frame_x(c, s);
    case 2:
        return frame_y(c, s);
    case 3:
        return frame_z(c, s);
    default:
        return math::Mat3::identity();
    }
}

FrameTransform euler_313_transform(const Euler313& a) noexcept {
    // R = M_w M_delta M_phi with M_w = [w]_3, M_delta = [delta]_1 and M_phi = [phi]_3, so
    // dR/dt = M_w' w' M_delta M_phi + M_w M_delta' delta' M_phi + M_w M_delta M_phi' phi'.
    const double cw = std::cos(a.w_rad);
    const double sw = std::sin(a.w_rad);
    const double cd = std::cos(a.delta_rad);
    const double sd = std::sin(a.delta_rad);
    const double cp = std::cos(a.phi_rad);
    const double sp = std::sin(a.phi_rad);
    const math::Mat3 m_w = frame_z(cw, sw);
    const math::Mat3 m_delta = frame_x(cd, sd);
    const math::Mat3 m_phi = frame_z(cp, sp);
    const math::Mat3 pole = m_delta * m_phi;
    const math::Mat3 rotation = m_w * pole;
    const math::Mat3 rate = frame_z_derivative(cw, sw) * pole * a.w_rate_rad_s +
                            m_w * frame_x_derivative(cd, sd) * m_phi * a.delta_rate_rad_s +
                            m_w * m_delta * frame_z_derivative(cp, sp) * a.phi_rate_rad_s;
    return {rotation, rate};
}

} // namespace usim::frames
