#pragma once

#include "usim/catalog/body_id.hpp"
#include "usim/catalog/quantity.hpp"

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>

namespace usim::catalog {

/// Classification of a body (SPEC section 7.5). Values are stable and shared with the C ABI.
enum class BodyKind : std::int32_t {
    barycenter = 0,   ///< A system barycenter (for example the Solar System or Earth-Moon barycenter).
    star = 1,         ///< A star.
    planet = 2,       ///< A planet.
    dwarf_planet = 3, ///< A dwarf planet.
    moon = 4,         ///< A natural satellite.
    asteroid = 5,     ///< An asteroid.
    comet = 6,        ///< A comet.
    spacecraft = 7,   ///< A spacecraft.
    star_system = 8,  ///< A star system treated as one object (Phase 9).
    galaxy = 9,       ///< A galaxy (Phase 9).
};

/// Lowercase name of the kind, for example "dwarf_planet".
[[nodiscard]] std::string_view to_string(BodyKind kind) noexcept;

/// A body in the catalog. Physical values carry their provenance and may be missing.
struct Body {
    BodyId id;                     ///< Unique identifier.
    std::string name;              ///< Display name, for example "Earth".
    BodyKind kind = BodyKind::barycenter; ///< Classification.
    std::optional<BodyId> primary; ///< The body this one orbits (SPEC section 7.5); none for a root.

    Quantity gm = Quantity::missing(Unit::km3_per_s2); ///< Gravitational parameter GM, km^3/s^2.
    Quantity mean_radius = Quantity::missing(Unit::km); ///< Mean radius, km.
    /// Triaxial radii in km, as in the IAU reports and SPICE BODYnnn_RADII: two equatorial radii (a, the
    /// largest, toward the prime meridian, and b) and the polar radius c.
    Quantity radius_a = Quantity::missing(Unit::km);
    Quantity radius_b = Quantity::missing(Unit::km); ///< Second equatorial radius (b), km.
    Quantity radius_c = Quantity::missing(Unit::km); ///< Polar radius (c), km.

    // Surface conditions (pack section SURF, ADR-0017). The reference surface is the ellipsoid of the radii above.

    /// Mean temperature at the reference surface, averaged over the body, K: over the solid surface, or at the
    /// reference pressure level of a body without one. Not representative of any one place.
    Quantity mean_temperature = Quantity::missing(Unit::kelvin);
    /// Atmospheric pressure at the solid surface, Pa. Present only when the reference surface is solid.
    Quantity surface_pressure = Quantity::missing(Unit::pascal);
    /// The pressure level the reference radii refer to, Pa. Present only when the reference surface is a level in
    /// the body's atmosphere, with no solid ground there.
    Quantity reference_pressure = Quantity::missing(Unit::pascal);
};

/// What a body's reference surface, the ellipsoid of its radii, is (ADR-0017).
enum class SurfaceKind : std::uint8_t {
    unknown = 0,        ///< No source in the pack says.
    solid = 1,          ///< Solid ground: the body has a surface pressure.
    pressure_level = 2, ///< A level in the atmosphere, without solid ground: the body has a reference pressure.
};

/// The kind of `body`'s reference surface, from its surface conditions: a pressure level when it has a reference
/// pressure, solid when it has a surface pressure, unknown with neither. A valid catalog never has both.
[[nodiscard]] SurfaceKind surface_kind(const Body& body) noexcept;

/// Lowercase name of the kind: "unknown", "solid" or "pressure_level".
[[nodiscard]] std::string_view to_string(SurfaceKind kind) noexcept;

/// Whether a body has an atmosphere, as its surface conditions show it (ADR-0018).
enum class Atmosphere : std::uint8_t {
    unknown = 0, ///< No source in the pack says.
    none = 1,    ///< The surface pressure is zero at the source's precision (a far thinner exosphere may exist).
    present = 2, ///< A positive surface pressure, or a reference surface that is a pressure level in it.
};

/// Whether `body` has an atmosphere: present when it has a reference pressure (its reference surface is a level in
/// the atmosphere) or a positive surface pressure, none when its surface pressure is zero, unknown otherwise.
[[nodiscard]] Atmosphere atmosphere(const Body& body) noexcept;

/// Lowercase name: "unknown", "none" or "present".
[[nodiscard]] std::string_view to_string(Atmosphere atmosphere) noexcept;

} // namespace usim::catalog
