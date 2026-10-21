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
};

} // namespace usim::catalog
