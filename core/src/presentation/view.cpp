#include "usim/presentation/view.hpp"

#include <cmath>

namespace usim::presentation {

std::string_view to_string(ScaleMode mode) noexcept {
    switch (mode) {
    case ScaleMode::true_scale:
        return "true-scale";
    case ScaleMode::enlarged_bodies:
        return "enlarged-bodies";
    }
    return "unknown";
}

Result<void> validate(const View& view) {
    if (!std::isfinite(view.origin_km.x) || !std::isfinite(view.origin_km.y) ||
        !std::isfinite(view.origin_km.z)) {
        return make_error(ErrorCode::invalid_argument, "the view origin must be finite");
    }
    if (!std::isfinite(view.km_per_render_unit) || !(view.km_per_render_unit > 0.0)) {
        return make_error(ErrorCode::invalid_argument,
                          "kilometers per render unit must be positive and finite");
    }
    if (to_string(view.axes) == "unknown") {
        return make_error(ErrorCode::invalid_argument, "unknown axis convention");
    }
    if (to_string(view.scale_mode) == "unknown") {
        return make_error(ErrorCode::invalid_argument, "unknown scale mode");
    }
    if (!std::isfinite(view.compression_distance) || view.compression_distance < 0.0) {
        return make_error(ErrorCode::invalid_argument,
                          "the compression distance must be finite and not negative");
    }
    if (!std::isfinite(view.body_scale) || !(view.body_scale >= 1.0)) {
        return make_error(ErrorCode::invalid_argument, "the body scale must be finite and at least 1");
    }
    return {};
}

math::Vec3 to_render_position(const View& view, const math::Vec3& position_km) noexcept {
    // The difference first, while both are in km: near the origin it is small and exact enough.
    return to_host(view.axes, (position_km - view.origin_km) / view.km_per_render_unit);
}

math::Vec3 to_render_velocity(const View& view, const math::Vec3& velocity_km_s) noexcept {
    return to_host(view.axes, velocity_km_s / view.km_per_render_unit);
}

double to_render_radius(const View& view, double radius_km) noexcept {
    const double radius = radius_km / view.km_per_render_unit;
    return view.scale_mode == ScaleMode::enlarged_bodies ? radius * view.body_scale : radius;
}

double distance_compression(const View& view, double distance) noexcept {
    const double c = view.compression_distance;
    if (!(c > 0.0) || !(distance > c)) {
        return 1.0;
    }
    return c * (1.0 + std::log(distance / c)) / distance;
}

math::Vec3 to_render_radii(const View& view, const math::Vec3& radii_km) noexcept {
    // The axis map permutes the axes and may flip signs; a length along an axis stays positive.
    const math::Vec3 host = to_host(view.axes, radii_km);
    return {to_render_radius(view, std::fabs(host.x)), to_render_radius(view, std::fabs(host.y)),
            to_render_radius(view, std::fabs(host.z))};
}

math::Quat to_render_rotation(const View& view, const math::Quat& rotation) noexcept {
    return to_host(view.axes, rotation);
}

} // namespace usim::presentation
