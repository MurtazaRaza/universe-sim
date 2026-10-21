#pragma once

#include <cmath>

namespace usim::math {

/// A 3-component double-precision vector. The units and frame are those of the quantity it holds
/// (for example `position_km` in ECLIPJ2000); the type itself is unit-agnostic.
/// Standard layout, so it matches `usim_vec3d` in the C ABI.
struct Vec3 {
    double x = 0.0; ///< First component.
    double y = 0.0; ///< Second component.
    double z = 0.0; ///< Third component.

    /// Exact component-wise equality.
    friend constexpr bool operator==(const Vec3&, const Vec3&) noexcept = default;

    /// Component-wise sum.
    [[nodiscard]] friend constexpr Vec3 operator+(const Vec3& a, const Vec3& b) noexcept {
        return {a.x + b.x, a.y + b.y, a.z + b.z};
    }

    /// Component-wise difference.
    [[nodiscard]] friend constexpr Vec3 operator-(const Vec3& a, const Vec3& b) noexcept {
        return {a.x - b.x, a.y - b.y, a.z - b.z};
    }

    /// Negation.
    [[nodiscard]] friend constexpr Vec3 operator-(const Vec3& a) noexcept { return {-a.x, -a.y, -a.z}; }

    /// Scaling by `s`.
    [[nodiscard]] friend constexpr Vec3 operator*(const Vec3& a, double s) noexcept {
        return {a.x * s, a.y * s, a.z * s};
    }

    /// Scaling by `s`.
    [[nodiscard]] friend constexpr Vec3 operator*(double s, const Vec3& a) noexcept { return a * s; }

    /// Division by `s`.
    [[nodiscard]] friend constexpr Vec3 operator/(const Vec3& a, double s) noexcept {
        return {a.x / s, a.y / s, a.z / s};
    }

    /// In-place sum.
    constexpr Vec3& operator+=(const Vec3& b) noexcept {
        *this = *this + b;
        return *this;
    }

    /// In-place difference.
    constexpr Vec3& operator-=(const Vec3& b) noexcept {
        *this = *this - b;
        return *this;
    }
};

/// Dot product.
[[nodiscard]] constexpr double dot(const Vec3& a, const Vec3& b) noexcept {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

/// Cross product (right-handed).
[[nodiscard]] constexpr Vec3 cross(const Vec3& a, const Vec3& b) noexcept {
    return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
}

/// Squared Euclidean length.
[[nodiscard]] constexpr double norm_squared(const Vec3& a) noexcept {
    return dot(a, a);
}

/// Euclidean length. Uses std::hypot, so it neither overflows nor underflows for large or tiny components.
[[nodiscard]] inline double norm(const Vec3& a) noexcept {
    return std::hypot(a.x, a.y, a.z);
}

/// Unit vector in the direction of `a`. Returns the zero vector unchanged if `a` has zero length.
[[nodiscard]] inline Vec3 normalized(const Vec3& a) noexcept {
    const double length = norm(a);
    return length > 0.0 ? a / length : a;
}

/// Euclidean distance between two points.
[[nodiscard]] inline double distance(const Vec3& a, const Vec3& b) noexcept {
    return norm(a - b);
}

} // namespace usim::math
