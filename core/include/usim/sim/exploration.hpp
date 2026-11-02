#pragma once

// Derived values for exploring the Solar System in a host (ADR-0016): two-body estimates of trips between
// bodies, points of a body's reference surface with the Sun seen from there, and the length of a body's day.
// Every value is computed from the pack's ephemeris, rotation models, GM and radii; nothing here is a new
// physical input.

#include "usim/catalog/body_id.hpp"
#include "usim/frames/frame_id.hpp"
#include "usim/math/vec3.hpp"
#include "usim/orbits/two_body.hpp"
#include "usim/presentation/axis_convention.hpp"
#include "usim/result.hpp"
#include "usim/sim/simulation.hpp"
#include "usim/surface/ellipsoid.hpp"
#include "usim/time/time_point.hpp"

#include <cstdint>

namespace usim::sim {

/// TransferEstimate flags. Values are stable and shared with the C ABI (USIM_TRANSFER_*).
enum TransferFlag : std::uint32_t {
    /// The trip starts at the center's surface: the destination orbits the departure body (the Earth to the
    /// Moon).
    /// departure_radius_km is the center's mean radius.
    transfer_from_surface = 1u << 0,
    /// The trip ends at the center's surface: the departure body orbits the destination (the Moon to the
    /// Earth).
    transfer_to_surface = 1u << 1,
    /// The transfer joins the orbits of the systems the bodies belong to, not of the bodies themselves: for a
    /// trip
    /// from the Earth to Io, the orbits of the Earth and Jupiter around the Sun.
    transfer_between_systems = 1u << 2,
    /// A state used is approximate (ADR-0008).
    transfer_approximate = 1u << 3,
};

/// A two-body estimate of a trip from one body to another at an instant (ADR-0016).
///
/// The orbit-center chain of a body is the body, the body it orbits (Simulation::default_orbit_center: a
/// moon's planet, a planet's star), that body's center and so on. The transfer goes around the first body the
/// two chains share (the center) and joins the orbits around it that lead to the two bodies: a Hohmann
/// transfer between circular coplanar orbits whose radii are the osculating semi-major axes of those orbits.
/// An orbit around the star is taken at the orbiting body's system barycenter where the catalog has one (the
/// Earth-Moon barycenter for the Earth), with GM the sum of the center's and the system's. When the center is
/// itself one of the two bodies, that end of the transfer is the center's surface (its mean radius).
///
/// This is the textbook first estimate of a mission, not a trajectory: it ignores the planets' eccentricities
/// and inclinations, launch windows, the bodies' own gravity at departure and arrival, and the final leg to a
/// moon.
struct TransferEstimate {
    catalog::BodyId center;    ///< The body the transfer goes around; its GM drives the transfer.
    catalog::BodyId departure; ///< The body whose orbit around the center the transfer leaves; the center
                               ///< itself with transfer_from_surface.
    catalog::BodyId arrival;   ///< The body whose orbit around the center it reaches; the center itself with
                               ///< transfer_to_surface.
    double departure_radius_km =
        0.0; ///< r1: the departure orbit's osculating semi-major axis, or the center's mean radius.
    double arrival_radius_km = 0.0; ///< r2: the same for the arrival.
    orbits::HohmannTransfer
        transfer; ///< Time and speed changes of the Hohmann transfer between r1 and r2 around the center.
    double distance_km =
        0.0; ///< Distance between the centers of the two requested bodies at the instant (geometric).
    double light_time_s = 0.0; ///< distance_km divided by the speed of light (constants::k_c_km_s).
    std::uint32_t flags = 0;   ///< TransferFlag bits.
};

/// The two-body estimate of a trip from `from` to `to` at `t` (TDB). Fails with ErrorCode::invalid_argument
/// when the two are the same body, ErrorCode::not_found for an unknown body or when a needed GM or radius is
/// missing, ErrorCode::unsupported when the bodies share no center with a GM or an orbit is not elliptic, and
/// like Simulation::state outside the coverage.
[[nodiscard]] Result<TransferEstimate> transfer_estimate(const Simulation& sim, catalog::BodyId from,
                                                         catalog::BodyId to, time::Instant t);

/// SurfaceSite flags. Values are stable and shared with the C ABI (USIM_SITE_*).
enum SurfaceSiteFlag : std::uint32_t {
    site_has_sun =
        1u << 0, ///< The star's position is known: the sun_* fields and local_solar_time_h are set.
    site_has_sun_size = 1u << 1, ///< The star has a mean radius: sun_angular_diameter_rad is set.
    site_has_gravity = 1u << 2,  ///< The body has a GM: gravity_km_s2 and escape_speed_km_s are set.
    site_approximate =
        1u << 3, ///< The body's position is approximate (ADR-0008), and with it the Sun's direction.
};

/// A point above a body's reference ellipsoid (surface::Ellipsoid from its triaxial radii, else a sphere of
/// its mean radius), with its local horizon, the star seen from there and the gravity of the reference
/// surface below it (ADR-0016). Vectors are in the display frame, mapped to the host's axes, relative to the
/// body's center.
struct SurfaceSite {
    surface::PlanetocentricPoint point; ///< Planetocentric latitude, east longitude and altitude of the site.
    double surface_radius_km = 0.0; ///< Distance from the center to the ellipsoid in the site's direction.
    math::Vec3 position_km{};       ///< The site relative to the body's center, km.
    math::Vec3 up{};                ///< Unit vector: the ellipsoid's outward normal below the site.
    math::Vec3 north{};             ///< Unit vector along the horizon toward the north pole.
    math::Vec3 east{};              ///< Unit vector along the horizon toward the east.
    math::Vec3
        sun_direction{}; ///< Unit vector from the site toward the star's center (geometric, no light time).
    double sun_elevation_rad =
        0.0; ///< The star's center above the horizon plane (negative below; no refraction).
    double sun_azimuth_rad = 0.0;          ///< From north toward east, [0, 2 pi).
    double sun_distance_km = 0.0;          ///< From the site to the star's center.
    double sun_angular_diameter_rad = 0.0; ///< 2 asin(R / d) with the star's mean radius R.
    double sunlight_ratio =
        0.0; ///< (1 au / d)^2: the star's light relative to a distance of one astronomical unit.
    /// Local solar time from the true (not mean) Sun, hours of a 24-hour local solar day: 12 when the star
    /// crosses the site's meridian, increasing with the body's rotation.
    double local_solar_time_h = 0.0;
    double gravity_km_s2 =
        0.0; ///< GM / r^2 at the reference surface below the site (r = surface_radius_km; no rotation).
    double escape_speed_km_s = 0.0; ///< sqrt(2 GM / r) from the reference surface below the site.
    std::uint32_t flags = 0;        ///< SurfaceSiteFlag bits.
};

/// The site at `point` of `body` at `t` (TDB), with vectors in `display_frame` (inertial) and `axes`. Fails
/// with ErrorCode::invalid_argument for a non-inertial display frame or non-finite coordinates,
/// ErrorCode::not_found when the body is unknown or has no rotation model or radius, and like
/// Simulation::orientation outside a model's span.
[[nodiscard]] Result<SurfaceSite> surface_site(const Simulation& sim, catalog::BodyId body,
                                               const surface::PlanetocentricPoint& point,
                                               frames::FrameId display_frame,
                                               presentation::AxisConvention axes, time::Instant t);

/// The site at the position `position_host_km` relative to `body`'s center (km, `display_frame`, host
/// `axes`): the inverse of SurfaceSite::position_km. Fails like surface_site(), and with
/// ErrorCode::invalid_argument for a zero or non-finite position.
[[nodiscard]] Result<SurfaceSite> surface_site_below(const Simulation& sim, catalog::BodyId body,
                                                     const math::Vec3& position_host_km,
                                                     frames::FrameId display_frame,
                                                     presentation::AxisConvention axes, time::Instant t);

/// DayLength flags. Values are stable and shared with the C ABI (USIM_DAY_*).
enum DayLengthFlag : std::uint32_t {
    /// The body's system orbits the star on an elliptic orbit: mean_solar_day_s and orbital_period_s are set.
    day_has_solar_day = 1u << 0,
    /// The body turns against the sense of its system's orbit around the star (its rotation and the orbital
    /// angular momentum point to opposite sides of the orbit plane), like Venus.
    day_retrograde = 1u << 1,
    /// The orbit used is approximate (ADR-0008).
    day_approximate = 1u << 2,
    /// The body has no IAU rotation elements, so the rotation rate is the rotation model's instantaneous rate,
    /// librations included, not the mean rate.
    day_instantaneous_rate = 1u << 3,
};

/// How long a body's days are at an instant (ADR-0016).
struct DayLength {
    /// One turn relative to the stars, s: 2 pi / |omega| with the mean rotation rate omega, the secular part of
    /// the IAU prime meridian polynomial (W1 + 2 W2 d) without its periodic terms. The instantaneous rate of
    /// the rotation model with day_instantaneous_rate.
    double sidereal_rotation_period_s = 0.0;
    /// Mean time from one local noon to the next, s: 2 pi / |n_rot - n_orb|, with n_rot = |omega| signed by
    /// the sense of rotation relative to the orbit and n_orb the orbit's mean motion. Checked against the
    /// Sun's hour angle followed over whole orbits (validation/test_exploration.cpp).
    double mean_solar_day_s = 0.0;
    /// Sidereal period of the osculating two-body orbit around the star of the body's system, s.
    double orbital_period_s = 0.0;
    /// The body whose orbit around the star gives orbital_period_s: the body's ancestor (or the body itself)
    /// whose primary is the star or the catalog's root, for example the Earth-Moon barycenter for the Earth
    /// and the Moon.
    catalog::BodyId orbiting_body;
    std::uint32_t flags = 0; ///< DayLengthFlag bits.
};

/// The day length of `body` at `t` (TDB). Fails with ErrorCode::not_found when the body is unknown or has no
/// rotation model, and ErrorCode::unsupported when it does not rotate. The solar day is left unset (no
/// day_has_solar_day) for the star itself, without a star with a GM, or when the orbit is not elliptic.
[[nodiscard]] Result<DayLength> day_length(const Simulation& sim, catalog::BodyId body, time::Instant t);

} // namespace usim::sim
