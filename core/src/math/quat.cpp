#include "usim/math/quat.hpp"

#include <cmath>

namespace usim::math {

Quat Quat::from_axis_angle(const Vec3& axis, double angle_rad) noexcept {
    const double length = norm(axis);
    if (!(length > 0.0)) {
        return identity();
    }
    const double half = 0.5 * angle_rad;
    const double s = std::sin(half) / length;
    return {std::cos(half), axis.x * s, axis.y * s, axis.z * s};
}

Quat Quat::from_matrix(const Mat3& r) noexcept {
    // Shepperd (1978): pick the largest of 4w^2, 4x^2, 4y^2, 4z^2 and derive the rest from it, which
    // avoids dividing by a small number.
    const double trace = r(0, 0) + r(1, 1) + r(2, 2);
    Quat q;
    if (trace >= r(0, 0) && trace >= r(1, 1) && trace >= r(2, 2)) {
        const double s = 2.0 * std::sqrt(1.0 + trace); // 4w
        q = {0.25 * s, (r(2, 1) - r(1, 2)) / s, (r(0, 2) - r(2, 0)) / s, (r(1, 0) - r(0, 1)) / s};
    } else if (r(0, 0) >= r(1, 1) && r(0, 0) >= r(2, 2)) {
        const double s = 2.0 * std::sqrt(1.0 + r(0, 0) - r(1, 1) - r(2, 2)); // 4x
        q = {(r(2, 1) - r(1, 2)) / s, 0.25 * s, (r(0, 1) + r(1, 0)) / s, (r(0, 2) + r(2, 0)) / s};
    } else if (r(1, 1) >= r(2, 2)) {
        const double s = 2.0 * std::sqrt(1.0 - r(0, 0) + r(1, 1) - r(2, 2)); // 4y
        q = {(r(0, 2) - r(2, 0)) / s, (r(0, 1) + r(1, 0)) / s, 0.25 * s, (r(1, 2) + r(2, 1)) / s};
    } else {
        const double s = 2.0 * std::sqrt(1.0 - r(0, 0) - r(1, 1) + r(2, 2)); // 4z
        q = {(r(1, 0) - r(0, 1)) / s, (r(0, 2) + r(2, 0)) / s, (r(1, 2) + r(2, 1)) / s, 0.25 * s};
    }
    q = normalized(q);
    return q.w < 0.0 ? -q : q;
}

double norm(const Quat& q) noexcept {
    return std::sqrt(dot(q, q));
}

Quat normalized(const Quat& q) noexcept {
    const double length = norm(q);
    if (!(length > 0.0)) {
        return Quat::identity();
    }
    return {q.w / length, q.x / length, q.y / length, q.z / length};
}

Vec3 rotate(const Quat& q, const Vec3& v) noexcept {
    // v' = v + 2w (u x v) + 2 u x (u x v), with u the vector part. Equivalent to q v q* for unit q.
    const Vec3 u = q.vector();
    const Vec3 t = cross(u, v) * 2.0;
    return v + t * q.w + cross(u, t);
}

Mat3 to_matrix(const Quat& q) noexcept {
    const double xx = q.x * q.x;
    const double yy = q.y * q.y;
    const double zz = q.z * q.z;
    const double xy = q.x * q.y;
    const double xz = q.x * q.z;
    const double yz = q.y * q.z;
    const double wx = q.w * q.x;
    const double wy = q.w * q.y;
    const double wz = q.w * q.z;
    return Mat3{{1.0 - 2.0 * (yy + zz), 2.0 * (xy - wz), 2.0 * (xz + wy), 2.0 * (xy + wz), 1.0 - 2.0 * (xx + zz),
                 2.0 * (yz - wx), 2.0 * (xz - wy), 2.0 * (yz + wx), 1.0 - 2.0 * (xx + yy)}};
}

Quat slerp(const Quat& a, const Quat& b, double t) noexcept {
    double cos_theta = dot(a, b);
    Quat end = b;
    if (cos_theta < 0.0) {
        end = -b;
        cos_theta = -cos_theta;
    }

    // Beyond this, sin(theta) is too small to divide by; linear interpolation is accurate there.
    constexpr double k_linear_threshold = 0.9995;
    double wa = 1.0 - t;
    double wb = t;
    if (cos_theta < k_linear_threshold) {
        const double theta = std::acos(cos_theta);
        const double sin_theta = std::sin(theta);
        wa = std::sin((1.0 - t) * theta) / sin_theta;
        wb = std::sin(t * theta) / sin_theta;
    }
    return normalized(
        {wa * a.w + wb * end.w, wa * a.x + wb * end.x, wa * a.y + wb * end.y, wa * a.z + wb * end.z});
}

double angle_between(const Quat& a, const Quat& b) noexcept {
    // The relative rotation is conj(a) * b; its angle is 2 atan2(|vector|, |w|). The absolute value
    // folds q and -q together. atan2 stays accurate for tiny angles, unlike acos.
    const Quat relative = conjugate(a) * b;
    return 2.0 * std::atan2(norm(relative.vector()), std::fabs(relative.w));
}

bool same_rotation(const Quat& a, const Quat& b, double tolerance_rad) noexcept {
    return angle_between(a, b) <= tolerance_rad;
}

} // namespace usim::math
