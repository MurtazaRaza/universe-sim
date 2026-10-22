#pragma once

#include "usim/math/quat.hpp"
#include "usim/math/vec3.hpp"
#include "usim/presentation/axis_convention.hpp"
#include "usim/result.hpp"

#include <cstdint>
#include <string_view>

namespace usim::presentation {

/// How bodies are sized for display (SPEC UC-06). Values are stable and shared with the C ABI.
enum class ScaleMode : std::int32_t {
    true_scale = 0,      ///< Real radii and real distances.
    enlarged_bodies = 1, ///< Radii multiplied by View::body_scale; distances stay real.
};

/// Lowercase name: "true-scale" or "enlarged-bodies".
[[nodiscard]] std::string_view to_string(ScaleMode mode) noexcept;

/// How core positions become host render coordinates (SPEC FR-SIM-10, ARCHITECTURE section 5): positions
/// are re-centered on a double-precision view origin, scaled to render units and mapped to the host's
/// axes, all in double precision. Values near the camera are then small, so a host can cast them to
/// single precision without visible loss (NFR-02): the floating-origin scheme.
struct View {
    /// The view origin, km, in the frame and relative to the center that the positions use.
    math::Vec3 origin_km{};
    /// Kilometers per render unit (for example 1e6 shows 1 million km as 1 unit). Positive and finite.
    double km_per_render_unit = 1.0;
    /// The host's axis convention.
    AxisConvention axes = AxisConvention::core;
    /// Body sizing.
    ScaleMode scale_mode = ScaleMode::true_scale;
    /// Radius factor in ScaleMode::enlarged_bodies, at least 1 and finite. Ignored in true scale.
    double body_scale = 1.0;
    /// Render distance from the origin beyond which distances are compressed (distance_compression()), so
    /// coordinates stay small while the picture seen from the origin is unchanged. 0 (the default) turns
    /// compression off; otherwise positive and finite.
    double compression_distance = 0.0;
};

/// Checks a view. Fails with ErrorCode::invalid_argument for a non-finite origin, a scale that is not
/// positive and finite, an unknown axis convention or scale mode, or a body scale below 1.
[[nodiscard]] Result<void> validate(const View& view);

/// A position (km, in the view's frame) as a render position: (position - origin) / km_per_render_unit
/// in host axes.
[[nodiscard]] math::Vec3 to_render_position(const View& view, const math::Vec3& position_km) noexcept;

/// A velocity (km/s) in render units per second, in host axes.
[[nodiscard]] math::Vec3 to_render_velocity(const View& view, const math::Vec3& velocity_km_s) noexcept;

/// A radius (km) in render units, enlarged in ScaleMode::enlarged_bodies.
[[nodiscard]] double to_render_radius(const View& view, double radius_km) noexcept;

/// The factor by which a point at render distance `distance` from the origin is pulled toward the origin,
/// along its line of sight: 1 up to view.compression_distance c, then c (1 + ln(distance / c)) / distance.
/// The compressed distance grows monotonically, so depth order is kept, and scaling a body's position and
/// size by the same factor keeps its direction and apparent size: a camera at the origin sees the same
/// image (ADR-0009).
[[nodiscard]] double distance_compression(const View& view, double distance) noexcept;

/// The three radii of a body (km) along its own axes x, y, z (a toward the prime meridian, b, and the polar
/// radius c), as lengths along the host's axes in render units, enlarged in ScaleMode::enlarged_bodies. A
/// host scales the body's mesh by these in the body's local space and then applies its orientation.
[[nodiscard]] math::Vec3 to_render_radii(const View& view, const math::Vec3& radii_km) noexcept;

/// A rotation (unit quaternion, scalar first) in host axes.
[[nodiscard]] math::Quat to_render_rotation(const View& view, const math::Quat& rotation) noexcept;

} // namespace usim::presentation
