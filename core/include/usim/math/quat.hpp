#pragma once

#include "usim/math/mat3.hpp"
#include "usim/math/vec3.hpp"

namespace usim::math {

/// A double-precision quaternion w + xi + yj + zk, scalar first.
///
/// Conventions (the same as SPICE, checked against CSPICE axisar and m2q in the unit tests):
/// - Hamilton product: i*j = k.
/// - A unit quaternion represents the active rotation v' = q v q*, so the rotation by angle `a` about the
///   unit axis `u` is (cos(a/2), sin(a/2) u). to_matrix() gives the matrix that rotates vectors the same way.
/// - q and -q represent the same rotation. Functions that produce quaternions from matrices return the
///   one with w >= 0; compare rotations with same_rotation(), not with ==.
struct Quat {
    double w = 1.0; ///< Scalar part.
    double x = 0.0; ///< i component.
    double y = 0.0; ///< j component.
    double z = 0.0; ///< k component.

    /// Exact component-wise equality (q and -q compare unequal; see same_rotation()).
    friend constexpr bool operator==(const Quat&, const Quat&) noexcept = default;

    /// The identity rotation.
    [[nodiscard]] static constexpr Quat identity() noexcept { return {}; }

    /// The rotation by `angle_rad` about `axis` (right-hand rule). The axis need not be unit length.
    /// A zero axis gives the identity.
    [[nodiscard]] static Quat from_axis_angle(const Vec3& axis, double angle_rad) noexcept;

    /// The rotation represented by a proper rotation matrix (orthonormal, determinant +1), with w >= 0.
    /// Uses Shepperd's method, which is accurate for every rotation angle. The result is normalized, so
    /// small deviations from orthonormality are tolerated.
    [[nodiscard]] static Quat from_matrix(const Mat3& rotation) noexcept;

    /// The vector part (x, y, z).
    [[nodiscard]] constexpr Vec3 vector() const noexcept { return {x, y, z}; }

    /// Hamilton product. As rotations, applying `a * b` equals applying `b` first, then `a`.
    [[nodiscard]] friend constexpr Quat operator*(const Quat& a, const Quat& b) noexcept {
        return {a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z, a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x, a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w};
    }

    /// Negation (the same rotation, opposite sign).
    [[nodiscard]] friend constexpr Quat operator-(const Quat& q) noexcept { return {-q.w, -q.x, -q.y, -q.z}; }
};

/// Four-dimensional dot product.
[[nodiscard]] constexpr double dot(const Quat& a, const Quat& b) noexcept {
    return a.w * b.w + a.x * b.x + a.y * b.y + a.z * b.z;
}

/// Conjugate (w, -x, -y, -z). For a unit quaternion this is the inverse rotation.
[[nodiscard]] constexpr Quat conjugate(const Quat& q) noexcept {
    return {q.w, -q.x, -q.y, -q.z};
}

/// Length sqrt(w^2 + x^2 + y^2 + z^2).
[[nodiscard]] double norm(const Quat& q) noexcept;

/// Unit quaternion in the direction of `q`. Returns the identity if `q` has zero length.
[[nodiscard]] Quat normalized(const Quat& q) noexcept;

/// Rotates `v` by the unit quaternion `q` (active rotation).
[[nodiscard]] Vec3 rotate(const Quat& q, const Vec3& v) noexcept;

/// The rotation matrix of the unit quaternion `q`: to_matrix(q) * v == rotate(q, v). Equal to SPICE q2m.
[[nodiscard]] Mat3 to_matrix(const Quat& q) noexcept;

/// Spherical linear interpolation between unit quaternions, t in [0, 1]. Takes the shorter arc: if
/// dot(a, b) < 0, `b` is negated first. Falls back to normalized linear interpolation when the
/// quaternions are nearly parallel. The result is normalized.
[[nodiscard]] Quat slerp(const Quat& a, const Quat& b, double t) noexcept;

/// Angle in radians, in [0, pi], of the rotation that takes unit quaternion `a` to `b`, ignoring sign.
[[nodiscard]] double angle_between(const Quat& a, const Quat& b) noexcept;

/// True if unit quaternions `a` and `b` represent the same rotation within `tolerance_rad`, treating q and
/// -q as equal.
[[nodiscard]] bool same_rotation(const Quat& a, const Quat& b, double tolerance_rad) noexcept;

} // namespace usim::math
