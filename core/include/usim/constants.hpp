#pragma once

// Definitional constants only. Every value here is exact by definition or fixed by convention, and
// carries its citation. Measured quantities (GM, radii, rotation elements, ...) never belong here:
// they come from data packs with their provenance (CLAUDE.md rule 2).

#include <numbers>

namespace usim::constants {

/// Astronomical unit in kilometers. Exact: IAU 2012 Resolution B2 (149 597 870 700 m).
inline constexpr double k_au_km = 149'597'870.700;

/// Speed of light in vacuum in km/s. Exact: definition of the metre, SI Brochure, 9th edition (BIPM, 2019).
inline constexpr double k_c_km_s = 299'792.458;

/// Seconds in a day of 86 400 SI seconds, the day used by Julian dates and the TDB, TT and TAI scales.
inline constexpr double k_seconds_per_day = 86'400.0;

/// Days in a Julian year. IAU definition (IAU Style Manual, 1989); the unit of the light-year.
inline constexpr double k_days_per_julian_year = 365.25;

/// Days in a Julian century, the time unit of the IAU rotation models (Archinal et al. 2018).
inline constexpr double k_days_per_julian_century = 36'525.0;

/// Light-year in kilometers: the distance light travels in vacuum in one Julian year (IAU definition).
inline constexpr double k_light_year_km = k_c_km_s * k_days_per_julian_year * k_seconds_per_day;

/// Julian date of the J2000.0 epoch, 2000-01-01T12:00:00. The standard epoch adopted by the IAU in 1976.
/// In this project the epoch is read on the TDB scale, as in SPICE ephemeris time (NAIF Time Required
/// Reading), so TDB seconds past J2000 equal SPICE ET.
inline constexpr double k_j2000_jd = 2'451'545.0;

/// TT - TAI in seconds. Exact: IAU 1991 Resolution A4, Recommendation IV. Also DELTET/DELTA_T_A in the
/// NAIF leap seconds kernel.
inline constexpr double k_tt_minus_tai_s = 32.184;

/// Obliquity of the ecliptic that defines the ECLIPJ2000 frame, in arcseconds: the IAU 1976 value at
/// J2000 (Lieske et al. 1977, A&A 58, 1), which SPICE uses for its built-in ECLIPJ2000 frame. Checked
/// against CSPICE pxform("J2000", "ECLIPJ2000") in the unit tests.
inline constexpr double k_eclipj2000_obliquity_arcsec = 84'381.448;

/// The ECLIPJ2000 obliquity in radians (1 arcsecond = pi / 648 000 rad).
inline constexpr double k_eclipj2000_obliquity_rad =
    k_eclipj2000_obliquity_arcsec * (std::numbers::pi / 648'000.0);

} // namespace usim::constants
