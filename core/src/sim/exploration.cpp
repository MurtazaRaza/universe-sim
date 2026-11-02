#include "usim/sim/exploration.hpp"

#include "usim/constants.hpp"
#include "usim/math/mat3.hpp"

#include <algorithm>
#include <cmath>
#include <numbers>
#include <string>
#include <vector>

namespace usim::sim {

namespace {

constexpr double k_two_pi = 2.0 * std::numbers::pi;

const catalog::Body* find(const Simulation& sim, catalog::BodyId id) noexcept {
    return sim.catalog().find(id);
}

Error unknown_body(catalog::BodyId id) {
    return make_error(ErrorCode::not_found, "no body " + catalog::to_string(id) + " in the catalog");
}

// The body, the body it orbits, that body's center and so on (Simulation::default_orbit_center), up to the
// root.
Result<std::vector<catalog::BodyId>> orbit_chain(const Simulation& sim, catalog::BodyId body) {
    if (find(sim, body) == nullptr) {
        return unknown_body(body);
    }
    std::vector<catalog::BodyId> chain{body};
    // Each step moves up the catalog hierarchy, so the chain is never longer than the catalog.
    while (chain.size() <= sim.catalog().size()) {
        const Result<catalog::BodyId> center = sim.default_orbit_center(chain.back());
        if (!center) {
            break; // the root, or a body with nothing to orbit
        }
        chain.push_back(center.value());
    }
    return chain;
}

// The body whose orbit around `center` stands for `body`'s: a planet's system barycenter when the center is
// the star and the catalog has one (the Earth-Moon barycenter for the Earth), else the body itself.
catalog::BodyId orbiting_body(const Simulation& sim, catalog::BodyId body, catalog::BodyId center) {
    const catalog::Body* entry = find(sim, body);
    const bool planet =
        entry->kind == catalog::BodyKind::planet || entry->kind == catalog::BodyKind::dwarf_planet;
    if (center != sim.light_source() || !planet || !entry->primary) {
        return body;
    }
    const catalog::Body* primary = find(sim, *entry->primary);
    const bool system_barycenter = primary != nullptr && primary->kind == catalog::BodyKind::barycenter &&
                                   primary->primary.has_value() && sim.ephemeris().contains(primary->id);
    return system_barycenter ? primary->id : body;
}

struct OrbitRadius {
    double radius_km = 0.0;
    bool approximate = false;
};

// The osculating semi-major axis of `body`'s orbit around `center` (with GM `center_gm`) at `t`.
Result<OrbitRadius> orbit_radius(const Simulation& sim, catalog::BodyId body, catalog::BodyId center,
                                 double center_gm, time::Instant t) {
    const catalog::BodyId orbiter = orbiting_body(sim, body, center);
    const Result<BodyState> state = sim.state(orbiter, center, frames::FrameId::icrf(), t);
    if (!state) {
        return state.error();
    }
    const catalog::Body* entry = find(sim, orbiter);
    const double gm = center_gm + entry->gm.value.value_or(0.0);
    const Result<double> a = orbits::semi_major_axis_km(state.value().state, gm);
    if (!a) {
        return make_error(a.error().code, "the orbit of " + catalog::to_string(orbiter) + " around " +
                                              catalog::to_string(center) + ": " + a.error().message);
    }
    return OrbitRadius{a.value(), state.value().quality.approximate};
}

Result<double> mean_radius_km(const Simulation& sim, catalog::BodyId body) {
    const catalog::Body* entry = find(sim, body);
    if (!entry->mean_radius.has_value()) {
        return make_error(ErrorCode::not_found, "no mean radius for " + catalog::to_string(body));
    }
    return *entry->mean_radius.value;
}

// The body's reference ellipsoid: its triaxial radii, else a sphere of its mean radius.
Result<surface::Ellipsoid> ellipsoid_of(const catalog::Body& body) {
    if (body.radius_a.has_value() && body.radius_b.has_value() && body.radius_c.has_value()) {
        return surface::Ellipsoid{*body.radius_a.value, *body.radius_b.value, *body.radius_c.value};
    }
    if (body.mean_radius.has_value()) {
        const double r = *body.mean_radius.value;
        return surface::Ellipsoid{r, r, r};
    }
    return make_error(ErrorCode::not_found, "no radius for " + catalog::to_string(body.id));
}

// The angular velocity, rad/s, of the frame that `transform` maps from, expressed in the frame it maps to:
// the axial vector of dR/dt R^T (the antisymmetric part, which is exact for a rotation and its rate).
math::Vec3 angular_velocity(const frames::FrameTransform& transform) noexcept {
    const math::Mat3 omega = transform.rotation_rate_per_s() * math::transpose(transform.rotation());
    return {0.5 * (omega(2, 1) - omega(1, 2)), 0.5 * (omega(0, 2) - omega(2, 0)),
            0.5 * (omega(1, 0) - omega(0, 1))};
}

} // namespace

Result<TransferEstimate> transfer_estimate(const Simulation& sim, catalog::BodyId from, catalog::BodyId to,
                                           time::Instant t) {
    if (from == to) {
        return make_error(ErrorCode::invalid_argument, "the departure and the destination are the same body");
    }
    const Result<std::vector<catalog::BodyId>> from_chain = orbit_chain(sim, from);
    if (!from_chain) {
        return from_chain.error();
    }
    const Result<std::vector<catalog::BodyId>> to_chain = orbit_chain(sim, to);
    if (!to_chain) {
        return to_chain.error();
    }

    // The first body the two chains share, and where it sits in each.
    std::size_t i = 0;
    std::size_t j = 0;
    bool found = false;
    for (std::size_t k = 0; k < from_chain.value().size() && !found; ++k) {
        const auto& other = to_chain.value();
        const auto at = std::find(other.begin(), other.end(), from_chain.value()[k]);
        if (at != other.end()) {
            i = k;
            j = static_cast<std::size_t>(at - other.begin());
            found = true;
        }
    }
    if (!found) {
        return make_error(ErrorCode::unsupported, "the bodies orbit no common center");
    }

    TransferEstimate estimate;
    estimate.center = from_chain.value()[i];
    const catalog::Body* center = find(sim, estimate.center);
    if (!center->primary) {
        return make_error(ErrorCode::unsupported, "the bodies share only the catalog's root as a center");
    }
    if (!center->gm.has_value()) {
        return make_error(ErrorCode::not_found, "no GM for " + catalog::to_string(estimate.center));
    }
    const double gm = *center->gm.value;

    const auto end_radius = [&](const std::vector<catalog::BodyId>& chain, std::size_t at,
                                catalog::BodyId& end) -> Result<double> {
        if (at == 0) {
            end = estimate.center;
            return mean_radius_km(sim, estimate.center);
        }
        end = chain[at - 1];
        const Result<OrbitRadius> orbit = orbit_radius(sim, end, estimate.center, gm, t);
        if (!orbit) {
            return orbit.error();
        }
        if (orbit.value().approximate) {
            estimate.flags |= transfer_approximate;
        }
        return orbit.value().radius_km;
    };
    const Result<double> r1 = end_radius(from_chain.value(), i, estimate.departure);
    if (!r1) {
        return r1.error();
    }
    const Result<double> r2 = end_radius(to_chain.value(), j, estimate.arrival);
    if (!r2) {
        return r2.error();
    }
    estimate.departure_radius_km = r1.value();
    estimate.arrival_radius_km = r2.value();
    if (i == 0) {
        estimate.flags |= transfer_from_surface;
    }
    if (j == 0) {
        estimate.flags |= transfer_to_surface;
    }
    if ((i > 0 && estimate.departure != from) || (j > 0 && estimate.arrival != to)) {
        estimate.flags |= transfer_between_systems;
    }

    // The transfer orbit belongs to a spacecraft of negligible mass: the center's GM alone.
    const Result<orbits::HohmannTransfer> transfer = orbits::hohmann_transfer(gm, r1.value(), r2.value());
    if (!transfer) {
        return transfer.error();
    }
    estimate.transfer = transfer.value();

    const Result<BodyState> apart = sim.state(to, from, frames::FrameId::icrf(), t);
    if (!apart) {
        return apart.error();
    }
    if (apart.value().quality.approximate) {
        estimate.flags |= transfer_approximate;
    }
    estimate.distance_km = math::norm(apart.value().state.position_km);
    estimate.light_time_s = estimate.distance_km / constants::k_c_km_s;
    return estimate;
}

Result<SurfaceSite> surface_site(const Simulation& sim, catalog::BodyId body,
                                 const surface::PlanetocentricPoint& point, frames::FrameId display_frame,
                                 presentation::AxisConvention axes, time::Instant t) {
    if (!display_frame.is_inertial()) {
        return make_error(ErrorCode::invalid_argument,
                          "a site's display frame must be the ICRF or ECLIPJ2000");
    }
    if (!std::isfinite(point.latitude_rad) || !std::isfinite(point.longitude_rad) ||
        !std::isfinite(point.altitude_km) || std::fabs(point.latitude_rad) > 0.5 * std::numbers::pi) {
        return make_error(
            ErrorCode::invalid_argument,
            "latitude, longitude and altitude must be finite, with the latitude in [-pi/2, pi/2]");
    }
    const catalog::Body* entry = find(sim, body);
    if (entry == nullptr) {
        return unknown_body(body);
    }
    const Result<surface::Ellipsoid> ellipsoid = ellipsoid_of(*entry);
    if (!ellipsoid) {
        return ellipsoid.error();
    }
    const Result<frames::FrameTransform> to_display =
        sim.frames().transform(frames::FrameId::body_fixed(body), display_frame, t);
    if (!to_display) {
        return to_display.error();
    }
    const math::Mat3& rotation = to_display.value().rotation();

    SurfaceSite site;
    site.point = point;
    site.point.longitude_rad = std::remainder(point.longitude_rad, k_two_pi);
    const math::Vec3 radial = surface::direction(point.latitude_rad, site.point.longitude_rad);
    site.surface_radius_km = surface::radius_along(ellipsoid.value(), radial);
    const surface::LocalFrame horizon =
        surface::local_frame(ellipsoid.value(), point.latitude_rad, site.point.longitude_rad);
    const math::Vec3 position = rotation * surface::position_km(ellipsoid.value(), site.point);
    const math::Vec3 up = rotation * horizon.up;
    const math::Vec3 north = rotation * horizon.north;
    const math::Vec3 east = rotation * horizon.east;
    site.position_km = presentation::to_host(axes, position);
    site.up = presentation::to_host(axes, up);
    site.north = presentation::to_host(axes, north);
    site.east = presentation::to_host(axes, east);

    const catalog::BodyId star = sim.light_source();
    if (star.is_valid() && star != body) {
        const Result<BodyState> sun = sim.state(star, body, display_frame, t);
        if (!sun && sun.error().code == ErrorCode::out_of_coverage) {
            return sun.error();
        }
        if (sun) {
            const math::Vec3 toward = sun.value().state.position_km - position;
            const double d = math::norm(toward);
            const math::Vec3 to_sun = toward / d;
            site.sun_direction = presentation::to_host(axes, to_sun);
            site.sun_distance_km = d;
            site.sun_elevation_rad = std::asin(std::clamp(math::dot(to_sun, up), -1.0, 1.0));
            const double azimuth = std::atan2(math::dot(to_sun, east), math::dot(to_sun, north));
            site.sun_azimuth_rad = azimuth < 0.0 ? azimuth + k_two_pi : azimuth;
            const double au_over_d = constants::k_au_km / d;
            site.sunlight_ratio = au_over_d * au_over_d;

            // The hour angle: the site's longitude minus the Sun's, both body-fixed, measured in the sense of
            // the body's rotation about its north pole, so that local time runs forward on a retrograde
            // rotator.
            const math::Vec3 sun_fixed = math::transpose(rotation) * to_sun;
            const double sun_longitude = std::atan2(sun_fixed.y, sun_fixed.x);
            const math::Vec3 pole = rotation * math::Vec3{0.0, 0.0, 1.0};
            const double sense = math::dot(angular_velocity(to_display.value()), pole) < 0.0 ? -1.0 : 1.0;
            const double hour_angle =
                std::remainder(sense * (site.point.longitude_rad - sun_longitude), k_two_pi);
            const double hours = 12.0 + 12.0 * hour_angle / std::numbers::pi;
            site.local_solar_time_h = hours >= 24.0 ? hours - 24.0 : hours;
            site.flags |= site_has_sun;
            if (sun.value().quality.approximate) {
                site.flags |= site_approximate;
            }

            const catalog::Body* star_entry = find(sim, star);
            if (star_entry->mean_radius.has_value() && d > *star_entry->mean_radius.value) {
                site.sun_angular_diameter_rad = 2.0 * std::asin(*star_entry->mean_radius.value / d);
                site.flags |= site_has_sun_size;
            }
        }
    }

    if (entry->gm.has_value()) {
        const double gm = *entry->gm.value;
        site.gravity_km_s2 = gm / (site.surface_radius_km * site.surface_radius_km);
        site.escape_speed_km_s = std::sqrt(2.0 * gm / site.surface_radius_km);
        site.flags |= site_has_gravity;
    }
    return site;
}

Result<SurfaceSite> surface_site_below(const Simulation& sim, catalog::BodyId body,
                                       const math::Vec3& position_host_km, frames::FrameId display_frame,
                                       presentation::AxisConvention axes, time::Instant t) {
    if (!display_frame.is_inertial()) {
        return make_error(ErrorCode::invalid_argument,
                          "a site's display frame must be the ICRF or ECLIPJ2000");
    }
    const double r = math::norm(position_host_km);
    if (!std::isfinite(r) || !(r > 0.0)) {
        return make_error(ErrorCode::invalid_argument, "the position must be finite and not at the center");
    }
    const catalog::Body* entry = find(sim, body);
    if (entry == nullptr) {
        return unknown_body(body);
    }
    const Result<surface::Ellipsoid> ellipsoid = ellipsoid_of(*entry);
    if (!ellipsoid) {
        return ellipsoid.error();
    }
    const Result<frames::FrameTransform> to_display =
        sim.frames().transform(frames::FrameId::body_fixed(body), display_frame, t);
    if (!to_display) {
        return to_display.error();
    }
    const math::Vec3 fixed =
        math::transpose(to_display.value().rotation()) * presentation::from_host(axes, position_host_km);
    return surface_site(sim, body, surface::planetocentric(ellipsoid.value(), fixed), display_frame, axes, t);
}

Result<DayLength> day_length(const Simulation& sim, catalog::BodyId body, time::Instant t) {
    if (find(sim, body) == nullptr) {
        return unknown_body(body);
    }
    const Result<frames::FrameTransform> to_icrf =
        sim.frames().transform(frames::FrameId::body_fixed(body), frames::FrameId::icrf(), t);
    if (!to_icrf) {
        return to_icrf.error();
    }
    // The mean rotation: the secular rate of the IAU prime meridian polynomial, W1 + 2 W2 d (deg/day), about the
    // pole. Its periodic terms (Mercury's and the Moon's forced librations) change the instantaneous rate by up
    // to about 1e-4, which would carry into the solar day. Bodies oriented by a binary PCK keep their IAU elements
    // in the pack where the PCK provides them; without any, the instantaneous rate is the only rate there is.
    DayLength day;
    const math::Vec3 pole = to_icrf.value().rotation() * math::Vec3{0.0, 0.0, 1.0};
    math::Vec3 omega = angular_velocity(to_icrf.value());
    if (const frames::IauRotationElements* elements = sim.frames().find_rotation(body);
        elements != nullptr && elements->pm_deg.size() >= 2) {
        const double days = t.seconds_since_j2000() / constants::k_seconds_per_day;
        const double w2 = elements->pm_deg.size() >= 3 ? elements->pm_deg[2] : 0.0;
        const double deg_per_day = elements->pm_deg[1] + 2.0 * w2 * days;
        omega = pole * (deg_per_day * (std::numbers::pi / 180.0) / constants::k_seconds_per_day);
    } else {
        day.flags |= day_instantaneous_rate;
    }
    const double rate = math::norm(omega);
    if (!std::isfinite(rate) || !(rate > 0.0)) {
        return make_error(ErrorCode::unsupported,
                          "the rotation model of " + catalog::to_string(body) + " does not rotate");
    }
    day.sidereal_rotation_period_s = k_two_pi / rate;

    // The body whose orbit around the star stands for the body's: the first one up the hierarchy whose
    // primary is the star or the root.
    const catalog::BodyId star = sim.light_source();
    const Result<std::vector<catalog::BodyId>> ancestry = sim.catalog().ancestry(body);
    for (const catalog::BodyId id : ancestry.value()) {
        const catalog::Body* entry = find(sim, id);
        if (!entry->primary) {
            break; // the root itself orbits nothing
        }
        const catalog::Body* primary = find(sim, *entry->primary);
        if (*entry->primary == star || !primary->primary) {
            day.orbiting_body = id;
            break;
        }
    }
    const catalog::Body* star_entry = star.is_valid() ? find(sim, star) : nullptr;
    if (!day.orbiting_body.is_valid() || day.orbiting_body == star || star_entry == nullptr ||
        !star_entry->gm.has_value()) {
        return day;
    }
    const Result<BodyState> orbit = sim.state(day.orbiting_body, star, frames::FrameId::icrf(), t);
    if (!orbit) {
        return orbit.error();
    }
    const double gm = *star_entry->gm.value + find(sim, day.orbiting_body)->gm.value.value_or(0.0);
    const Result<double> a = orbits::semi_major_axis_km(orbit.value().state, gm);
    if (!a) {
        return day; // not on an elliptic orbit: no solar day
    }
    day.orbital_period_s = orbits::orbital_period_s(a.value(), gm);
    const math::Vec3 momentum =
        math::cross(orbit.value().state.position_km, orbit.value().state.velocity_km_s);
    const bool retrograde = math::dot(omega, momentum) < 0.0;
    const double rotation_rate = retrograde ? -rate : rate;
    const double synodic_rate = std::fabs(rotation_rate - k_two_pi / day.orbital_period_s);
    if (synodic_rate > 0.0) {
        day.mean_solar_day_s = k_two_pi / synodic_rate;
        day.flags |= day_has_solar_day;
    }
    if (retrograde) {
        day.flags |= day_retrograde;
    }
    if (orbit.value().quality.approximate) {
        day.flags |= day_approximate;
    }
    return day;
}

} // namespace usim::sim
