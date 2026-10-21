#pragma once

#include "usim/math/vec3.hpp"

#include <array>
#include <cmath>
#include <cstddef>

namespace usim::math {

/// A 3x3 double-precision matrix, stored row-major. Used mainly for rotations between frames.
///
/// Rotation convention: matrices act on column vectors (`v' = M * v`). A frame rotation matrix that
/// takes coordinates in frame A to coordinates in frame B is written `a_to_b`, matching SPICE pxform.
struct Mat3 {
    std::array<double, 9> m{}; ///< Elements in row-major order: m[row * 3 + col].

    /// Exact element-wise equality.
    friend constexpr bool operator==(const Mat3&, const Mat3&) noexcept = default;

    /// The identity matrix.
    [[nodiscard]] static constexpr Mat3 identity() noexcept { return Mat3{{1, 0, 0, 0, 1, 0, 0, 0, 1}}; }

    /// A matrix with the given rows.
    [[nodiscard]] static constexpr Mat3 from_rows(const Vec3& r0, const Vec3& r1, const Vec3& r2) noexcept {
        return Mat3{{r0.x, r0.y, r0.z, r1.x, r1.y, r1.z, r2.x, r2.y, r2.z}};
    }

    /// A matrix with the given columns.
    [[nodiscard]] static constexpr Mat3 from_columns(const Vec3& c0, const Vec3& c1, const Vec3& c2) noexcept {
        return Mat3{{c0.x, c1.x, c2.x, c0.y, c1.y, c2.y, c0.z, c1.z, c2.z}};
    }

    /// Active rotation by `angle_rad` about +X: rotates vectors counterclockwise when viewed from +X
    /// (right-hand rule). The frame rotation SPICE calls ROTATE(angle, 1) is rotation_x(-angle).
    [[nodiscard]] static Mat3 rotation_x(double angle_rad) noexcept {
        const double c = std::cos(angle_rad);
        const double s = std::sin(angle_rad);
        return Mat3{{1, 0, 0, 0, c, -s, 0, s, c}};
    }

    /// Active rotation by `angle_rad` about +Y (right-hand rule).
    [[nodiscard]] static Mat3 rotation_y(double angle_rad) noexcept {
        const double c = std::cos(angle_rad);
        const double s = std::sin(angle_rad);
        return Mat3{{c, 0, s, 0, 1, 0, -s, 0, c}};
    }

    /// Active rotation by `angle_rad` about +Z (right-hand rule).
    [[nodiscard]] static Mat3 rotation_z(double angle_rad) noexcept {
        const double c = std::cos(angle_rad);
        const double s = std::sin(angle_rad);
        return Mat3{{c, -s, 0, s, c, 0, 0, 0, 1}};
    }

    /// Element at `row`, `col` (both 0 to 2).
    [[nodiscard]] constexpr double operator()(std::size_t row, std::size_t col) const noexcept {
        return m[row * 3 + col];
    }

    /// Mutable element at `row`, `col` (both 0 to 2).
    [[nodiscard]] constexpr double& operator()(std::size_t row, std::size_t col) noexcept {
        return m[row * 3 + col];
    }

    /// Row `i` (0 to 2).
    [[nodiscard]] constexpr Vec3 row(std::size_t i) const noexcept {
        return {m[i * 3], m[i * 3 + 1], m[i * 3 + 2]};
    }

    /// Column `j` (0 to 2).
    [[nodiscard]] constexpr Vec3 column(std::size_t j) const noexcept { return {m[j], m[3 + j], m[6 + j]}; }

    /// Matrix-vector product.
    [[nodiscard]] friend constexpr Vec3 operator*(const Mat3& a, const Vec3& v) noexcept {
        return {dot(a.row(0), v), dot(a.row(1), v), dot(a.row(2), v)};
    }

    /// Matrix product: applying the result equals applying `b` first, then `a`.
    [[nodiscard]] friend constexpr Mat3 operator*(const Mat3& a, const Mat3& b) noexcept {
        Mat3 r;
        for (std::size_t i = 0; i < 3; ++i) {
            for (std::size_t j = 0; j < 3; ++j) {
                r(i, j) = a(i, 0) * b(0, j) + a(i, 1) * b(1, j) + a(i, 2) * b(2, j);
            }
        }
        return r;
    }

    /// Element-wise sum.
    [[nodiscard]] friend constexpr Mat3 operator+(const Mat3& a, const Mat3& b) noexcept {
        Mat3 r;
        for (std::size_t k = 0; k < 9; ++k) {
            r.m[k] = a.m[k] + b.m[k];
        }
        return r;
    }

    /// Element-wise scaling.
    [[nodiscard]] friend constexpr Mat3 operator*(const Mat3& a, double s) noexcept {
        Mat3 r;
        for (std::size_t k = 0; k < 9; ++k) {
            r.m[k] = a.m[k] * s;
        }
        return r;
    }
};

/// Transpose. For a rotation matrix this is its inverse.
[[nodiscard]] constexpr Mat3 transpose(const Mat3& a) noexcept {
    return Mat3::from_columns(a.row(0), a.row(1), a.row(2));
}

/// Determinant. +1 for a proper rotation, -1 for a rotation combined with a reflection.
[[nodiscard]] constexpr double determinant(const Mat3& a) noexcept {
    return dot(a.row(0), cross(a.row(1), a.row(2)));
}

/// Largest absolute element-wise difference. Used to compare matrices within a tolerance.
[[nodiscard]] inline double max_abs_difference(const Mat3& a, const Mat3& b) noexcept {
    double worst = 0.0;
    for (std::size_t k = 0; k < 9; ++k) {
        worst = std::fmax(worst, std::fabs(a.m[k] - b.m[k]));
    }
    return worst;
}

} // namespace usim::math
