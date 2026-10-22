#include "usim/catalog/sky.hpp"

#include <cmath>

namespace usim::catalog {

math::Vec3 star_direction_icrf(const Star& star, double epoch_tdb_s, double t_tdb_s) noexcept {
    const double cos_ra = std::cos(star.ra_rad);
    const double sin_ra = std::sin(star.ra_rad);
    const double cos_dec = std::cos(star.dec_rad);
    const double sin_dec = std::sin(star.dec_rad);
    const math::Vec3 direction{cos_dec * cos_ra, cos_dec * sin_ra, sin_dec};
    if (!star.proper_motion) {
        return direction;
    }
    // Unit vectors of increasing right ascension and declination at the catalog position.
    const math::Vec3 east{-sin_ra, cos_ra, 0.0};
    const math::Vec3 north{-sin_dec * cos_ra, -sin_dec * sin_ra, cos_dec};
    const double dt = t_tdb_s - epoch_tdb_s;
    const math::Vec3 moved = direction + east * (star.proper_motion->ra_cos_dec_rad_per_s * dt) +
                             north * (star.proper_motion->dec_rad_per_s * dt);
    return moved / math::norm(moved);
}

} // namespace usim::catalog
