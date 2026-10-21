#include "usim/constants.hpp"

#include "fixtures/spice_reference.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>

using Catch::Matchers::WithinAbs;
using Catch::Matchers::WithinRel;
namespace c = usim::constants;
namespace spice = usim::test::spice;

TEST_CASE("the astronomical unit and the speed of light have their defined SI values", "[constants]") {
    // IAU 2012 Resolution B2: 149 597 870 700 m. SI: 299 792 458 m/s.
    CHECK(c::k_au_km * 1000.0 == 149'597'870'700.0);
    CHECK(c::k_c_km_s * 1000.0 == 299'792'458.0);
}

TEST_CASE("the light-year is c times one Julian year", "[constants]") {
    // 299 792 458 m/s * 31 557 600 s = 9 460 730 472 580 800 m (IAU).
    CHECK_THAT(c::k_light_year_km, WithinRel(9'460'730'472'580.8, 1e-15));
}

TEST_CASE("time constants", "[constants]") {
    CHECK(c::k_seconds_per_day == 86'400.0);
    CHECK(c::k_days_per_julian_year * 100.0 == c::k_days_per_julian_century);
    CHECK(c::k_j2000_jd == 2'451'545.0);
    // The leap seconds kernel carries the same TT - TAI.
    CHECK(c::k_tt_minus_tai_s == spice::k_delta_t_a_s);
}

TEST_CASE("the ECLIPJ2000 obliquity reproduces the SPICE frame rotation", "[constants]") {
    // pxform("J2000", "ECLIPJ2000") has cos(eps) and sin(eps) in its lower-right block.
    CHECK_THAT(std::cos(c::k_eclipj2000_obliquity_rad), WithinAbs(spice::k_icrf_to_eclipj2000[1][1], 2e-16));
    CHECK_THAT(std::sin(c::k_eclipj2000_obliquity_rad), WithinAbs(spice::k_icrf_to_eclipj2000[1][2], 2e-16));
    CHECK_THAT(c::k_eclipj2000_obliquity_arcsec / 3600.0, WithinAbs(23.0 + 26.0 / 60.0 + 21.448 / 3600.0, 1e-12));
}
