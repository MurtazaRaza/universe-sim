#pragma once

#include "usim/math/mat3.hpp"
#include "usim/math/quat.hpp"
#include "usim/math/vec3.hpp"

#include <cstdint>
#include <string_view>

namespace usim::presentation {

/// Coordinate conventions of rendering hosts (ARCHITECTURE section 5.4). The core is right-handed with Z
/// up (the ECLIPJ2000 pole); a host's convention maps core vectors to host vectors through a fixed signed
/// permutation matrix S: v_host = S v_core. Rotations map as R_host = S R_core S^T, which for unit
/// quaternions is (w, det(S) S u) with u the vector part.
///
/// Values are stable and shared with the C ABI. Only the axes change here; unit scaling (for example
/// Unreal's centimeters) is a separate presentation step.
enum class AxisConvention : std::int32_t {
    core = 0,   ///< Identity: right-handed, Z up (CLI, C++ hosts).
    unity = 1,  ///< Unity: left-handed, Y up. v = (x, z, y); q = (w, -x, -z, -y).
    godot = 2,  ///< Godot: right-handed, Y up. v = (x, z, -y); q = (w, x, z, -y).
    unreal = 3, ///< Unreal: left-handed, Z up. v = (x, -y, z); q = (w, -x, y, -z).
};

/// Lowercase name, for example "unity".
[[nodiscard]] std::string_view to_string(AxisConvention convention) noexcept;

/// The axis map S of `convention` (v_host = S v_core).
[[nodiscard]] math::Mat3 axis_matrix(AxisConvention convention) noexcept;

/// True if the host's coordinates are left-handed (det(S) = -1).
[[nodiscard]] bool is_left_handed(AxisConvention convention) noexcept;

/// Maps a core vector (position, velocity, direction) to host axes.
[[nodiscard]] math::Vec3 to_host(AxisConvention convention, const math::Vec3& v) noexcept;

/// Maps a core rotation (unit quaternion, scalar first) to the same physical rotation in host axes.
[[nodiscard]] math::Quat to_host(AxisConvention convention, const math::Quat& q) noexcept;

/// Maps a host vector back to core axes (the inverse of to_host()).
[[nodiscard]] math::Vec3 from_host(AxisConvention convention, const math::Vec3& v) noexcept;

/// Maps a host rotation back to core axes (the inverse of to_host()).
[[nodiscard]] math::Quat from_host(AxisConvention convention, const math::Quat& q) noexcept;

} // namespace usim::presentation
