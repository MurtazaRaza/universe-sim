#pragma once

#include "usim/math/vec3.hpp"

namespace usim::surface {

/// A body's triaxial reference ellipsoid in its body-fixed frame (IAU convention, Archinal et al. 2018):
/// semi-axes a along +x (toward the prime meridian), b along +y and c along +z (the north pole), km. A sphere
/// has a = b = c.
struct Ellipsoid {
    double a_km = 0.0; ///< Equatorial radius toward the prime meridian, km.
    double b_km = 0.0; ///< Second equatorial radius, km.
    double c_km = 0.0; ///< Polar radius, km.
};

/// Planetocentric coordinates of a point in a body-fixed frame (the coordinates of the surface maps,
/// ADR-0014).
struct PlanetocentricPoint {
    double latitude_rad = 0.0;  ///< Angle above the equatorial plane seen from the center, [-pi/2, pi/2].
    double longitude_rad = 0.0; ///< East longitude from the prime meridian, [-pi, pi].
    double altitude_km = 0.0;   ///< Height above the ellipsoid along the direction from the center, km.
};

/// The local horizon at a point of the ellipsoid: unit vectors in the body-fixed frame.
struct LocalFrame {
    math::Vec3 up;   ///< The ellipsoid's outward normal: the horizon is the plane perpendicular to it.
    math::Vec3 east; ///< Along the horizon toward the east: the pole axis crossed with up, normalized.
    /// Along the horizon toward the north pole: up x east, so that (east, north, up) is right-handed. At a
    /// pole it takes its limit along the meridian of the given longitude.
    math::Vec3 north;
};

/// The unit vector from the center toward planetocentric latitude and east longitude (radians).
[[nodiscard]] math::Vec3 direction(double latitude_rad, double longitude_rad) noexcept;

/// The distance from the center to the ellipsoid along the unit vector `unit_direction`, km:
/// 1 / sqrt((x / a)^2 + (y / b)^2 + (z / c)^2). Precondition: positive radii.
[[nodiscard]] double radius_along(const Ellipsoid& ellipsoid, const math::Vec3& unit_direction) noexcept;

/// The body-fixed position of `point`, km.
[[nodiscard]] math::Vec3 position_km(const Ellipsoid& ellipsoid, const PlanetocentricPoint& point) noexcept;

/// The planetocentric coordinates of the body-fixed position `position_km` (km, not zero): the inverse of
/// position_km().
[[nodiscard]] PlanetocentricPoint planetocentric(const Ellipsoid& ellipsoid,
                                                 const math::Vec3& position_km) noexcept;

/// The local horizon at the ellipsoid point at planetocentric latitude and east longitude (radians).
[[nodiscard]] LocalFrame local_frame(const Ellipsoid& ellipsoid, double latitude_rad,
                                     double longitude_rad) noexcept;

} // namespace usim::surface
