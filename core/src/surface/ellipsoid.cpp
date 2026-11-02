#include "usim/surface/ellipsoid.hpp"

#include <cmath>

namespace usim::surface {

math::Vec3 direction(double latitude_rad, double longitude_rad) noexcept {
    const double cos_lat = std::cos(latitude_rad);
    return {cos_lat * std::cos(longitude_rad), cos_lat * std::sin(longitude_rad), std::sin(latitude_rad)};
}

double radius_along(const Ellipsoid& ellipsoid, const math::Vec3& unit_direction) noexcept {
    const double x = unit_direction.x / ellipsoid.a_km;
    const double y = unit_direction.y / ellipsoid.b_km;
    const double z = unit_direction.z / ellipsoid.c_km;
    return 1.0 / std::sqrt(x * x + y * y + z * z);
}

math::Vec3 position_km(const Ellipsoid& ellipsoid, const PlanetocentricPoint& point) noexcept {
    const math::Vec3 d = direction(point.latitude_rad, point.longitude_rad);
    return d * (radius_along(ellipsoid, d) + point.altitude_km);
}

PlanetocentricPoint planetocentric(const Ellipsoid& ellipsoid, const math::Vec3& position_km) noexcept {
    const double r = math::norm(position_km);
    PlanetocentricPoint point;
    // atan2 rather than asin: full precision near the poles.
    point.latitude_rad = std::atan2(position_km.z, std::hypot(position_km.x, position_km.y));
    point.longitude_rad = std::atan2(position_km.y, position_km.x);
    point.altitude_km = r - radius_along(ellipsoid, position_km / r);
    return point;
}

LocalFrame local_frame(const Ellipsoid& ellipsoid, double latitude_rad, double longitude_rad) noexcept {
    const math::Vec3 d = direction(latitude_rad, longitude_rad);
    // The gradient of (x/a)^2 + (y/b)^2 + (z/c)^2 at a point along d: (x/a^2, y/b^2, z/c^2), up to a factor.
    const math::Vec3 gradient{d.x / (ellipsoid.a_km * ellipsoid.a_km),
                              d.y / (ellipsoid.b_km * ellipsoid.b_km),
                              d.z / (ellipsoid.c_km * ellipsoid.c_km)};
    LocalFrame frame;
    frame.up = math::normalized(gradient);
    // East is the pole axis crossed with up: z x up = (-up.y, up.x, 0). Its components need no subtraction,
    // so its direction stays accurate however close the point is to a pole, and it tends to the right limit
    // there (the latitude pi/2 has a cosine of about 6e-17, not 0). Only an exact zero falls back to that
    // limit: the gradient's horizontal direction is (cos(lon) / a^2, sin(lon) / b^2), and east is z crossed
    // with it.
    double horizontal = std::hypot(frame.up.x, frame.up.y);
    math::Vec3 east{-frame.up.y, frame.up.x, 0.0};
    if (!(horizontal > 0.0)) {
        const double gx = std::cos(longitude_rad) / (ellipsoid.a_km * ellipsoid.a_km);
        const double gy = std::sin(longitude_rad) / (ellipsoid.b_km * ellipsoid.b_km);
        east = {-gy, gx, 0.0};
        horizontal = std::hypot(gx, gy);
    }
    frame.east = east / horizontal;
    // North completes the right-handed set (east, north, up), like (x, y, z).
    frame.north = math::cross(frame.up, frame.east);
    return frame;
}

} // namespace usim::surface
