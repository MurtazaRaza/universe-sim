#pragma once

#include "usim/catalog/body_id.hpp"
#include "usim/catalog/quantity.hpp"
#include "usim/math/vec3.hpp"

#include <optional>
#include <string>
#include <vector>

namespace usim::catalog {

/// Proper motion of a star in the tangent plane at its catalog position.
struct ProperMotion {
    double ra_cos_dec_rad_per_s = 0.0; ///< Rate of right ascension times cos(declination), rad/s.
    double dec_rad_per_s = 0.0;        ///< Rate of declination, rad/s.
};

/// One star of a star catalog (DATA_PACK.md, section STAR; SPEC FR-REN-09).
struct Star {
    BodyId id;                ///< Catalog number, for example IdNamespace::hr for the Bright Star Catalogue.
    std::string designation;  ///< As written in the catalog (for example "Alp CMa"); may be empty.
    double ra_rad = 0.0;      ///< Right ascension at the catalog epoch, ICRF, rad, in [0, 2 pi).
    double dec_rad = 0.0;     ///< Declination at the catalog epoch, ICRF, rad.
    std::optional<ProperMotion> proper_motion; ///< Absent when the catalog gives none.
    double v_magnitude = 0.0;                  ///< V magnitude.
    std::optional<double> b_minus_v;           ///< B-V color index, magnitudes; absent when not measured.
};

/// The stars of a pack, with the provenance shared by all of them.
struct StarCatalog {
    std::vector<Star> stars;   ///< Sorted by id.
    double epoch_tdb_s = 0.0;  ///< Epoch of the positions, TDB seconds past J2000.
    SourceRef source;          ///< The catalog file and its columns.
    std::string derivation;    ///< How the catalog's columns became the values.
};

/// Unit vector toward `star` in the ICRF at `t_tdb_s` (TDB seconds past J2000): the catalog direction moved
/// linearly in its tangent plane by the proper motion over t - epoch, then normalized. Parallax and radial
/// velocity are not modeled: a direction for a sky background, not astrometry.
[[nodiscard]] math::Vec3 star_direction_icrf(const Star& star, double epoch_tdb_s, double t_tdb_s) noexcept;

/// The normal optical depth of a ring region: the range a source gives (dimensionless).
struct OpticalDepth {
    double smallest = 0.0; ///< Smallest normal optical depth.
    double largest = 0.0;  ///< Largest normal optical depth.
    SourceRef source;      ///< Where the range comes from.
};

/// A ring region, or a gap within one, in the equatorial plane of a body (the XY plane of its IAU frame):
/// visual-only geometry (DATA_PACK.md, section RING; P6-04).
struct Ring {
    BodyId body;                  ///< The planet.
    std::string name;             ///< For example "C Ring" or "Encke Gap".
    bool gap = false;             ///< True for an empty region within a ring.
    double inner_radius_km = 0.0; ///< Inner edge, km from the body's center.
    double outer_radius_km = 0.0; ///< Outer edge, km from the body's center.
    SourceRef radii_source;       ///< Where the radii come from (with their derivation, if any).
    std::optional<OpticalDepth> optical_depth; ///< Absent for gaps and when no source gives one.
};

} // namespace usim::catalog
