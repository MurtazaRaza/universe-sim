// P4-03: evaluation of IAU rotation elements, on synthetic elements (the real PCK is compared with CSPICE
// in the validation tests, VAL-04). The coefficients are made up for the tests and are not physical data.

#include "usim/constants.hpp"
#include "usim/frames/frame_system.hpp"
#include "usim/frames/iau_rotation.hpp"
#include "usim/math/angle.hpp"
#include "usim/math/mat3.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>

using Catch::Matchers::WithinAbs;
using Catch::Matchers::WithinRel;
using usim::catalog::BodyId;
using usim::frames::FrameId;
using usim::frames::FrameSystem;
using usim::frames::FrameTransform;
using usim::frames::IauRotationElements;
using usim::math::Mat3;
using usim::time::Instant;
namespace m = usim::math;

namespace {

constexpr double k_day_s = usim::constants::k_seconds_per_day;
constexpr double k_century_s = usim::constants::k_days_per_julian_century * k_day_s;

// Elements with every kind of term: quadratic polynomials, three phase angles of degree 2, and nutation
// arrays of different lengths.
IauRotationElements full_elements() {
    IauRotationElements e;
    e.body = BodyId::naif(1000);
    e.pole_ra_deg = {40.0, -0.5, 0.01};
    e.pole_dec_deg = {60.0, 0.25, -0.02};
    e.pm_deg = {100.0, 350.0, 1.0e-9};
    e.phase_degree = 2;
    e.nut_prec_angles_deg = {10.0, 2000.0, 1.0, 20.0, -500.0, 0.0, 30.0, 40000.0, -3.0};
    e.angles_body = BodyId::naif(1000);
    e.nut_prec_ra_deg = {0.5, 0.0, 0.1};
    e.nut_prec_dec_deg = {0.2, -0.3};
    e.nut_prec_pm_deg = {1.5};
    return e;
}

} // namespace

TEST_CASE("IAU angles follow the polynomial and periodic terms", "[frames][iau]") {
    const IauRotationElements e = full_elements();
    const double t_s = 0.37 * k_century_s;
    const double days = t_s / k_day_s;
    const double centuries = 0.37;
    const auto a = usim::frames::iau_angles(e, Instant::from_seconds_since_j2000(t_s));

    double theta[3];
    for (int j = 0; j < 3; ++j) {
        const double* c = &e.nut_prec_angles_deg[static_cast<std::size_t>(3 * j)];
        theta[j] = m::deg_to_rad(c[0] + c[1] * centuries + c[2] * centuries * centuries);
    }
    const double ra = 40.0 - 0.5 * centuries + 0.01 * centuries * centuries + 0.5 * std::sin(theta[0]) +
                      0.1 * std::sin(theta[2]);
    const double dec = 60.0 + 0.25 * centuries - 0.02 * centuries * centuries + 0.2 * std::cos(theta[0]) -
                       0.3 * std::cos(theta[1]);
    const double w = 100.0 + 350.0 * days + 1.0e-9 * days * days + 1.5 * std::sin(theta[0]);
    CHECK_THAT(a.pole_ra_rad, WithinAbs(m::deg_to_rad(ra), 1e-14));
    CHECK_THAT(a.pole_dec_rad, WithinAbs(m::deg_to_rad(dec), 1e-14));
    CHECK_THAT(a.prime_meridian_rad, WithinRel(m::deg_to_rad(w), 1e-15));
}

TEST_CASE("IAU angle rates equal the numerical derivative", "[frames][iau]") {
    const IauRotationElements e = full_elements();
    const Instant t = Instant::from_seconds_since_j2000(-1.234e9);
    const double h = 10.0;
    const auto a = usim::frames::iau_angles(e, t);
    const auto before = usim::frames::iau_angles(e, t.plus_seconds(-h));
    const auto after = usim::frames::iau_angles(e, t.plus_seconds(h));
    CHECK_THAT(a.pole_ra_rate_rad_s, WithinRel((after.pole_ra_rad - before.pole_ra_rad) / (2 * h), 1e-5));
    CHECK_THAT(a.pole_dec_rate_rad_s, WithinRel((after.pole_dec_rad - before.pole_dec_rad) / (2 * h), 1e-5));
    CHECK_THAT(a.prime_meridian_rate_rad_s,
               WithinRel((after.prime_meridian_rad - before.prime_meridian_rad) / (2 * h), 1e-8));
}

TEST_CASE("a pole at the ICRF pole gives a pure rotation about Z", "[frames][iau]") {
    // alpha_0 = -90 deg and delta_0 = 90 deg make the pole rotations cancel: R = [W]_3.
    IauRotationElements e;
    e.body = BodyId::naif(1000);
    e.pole_ra_deg = {-90.0};
    e.pole_dec_deg = {90.0};
    e.pm_deg = {30.0, 360.0};
    const Instant t = Instant::from_seconds_since_j2000(0.25 * k_day_s); // W = 30 + 90 deg
    const FrameTransform x = usim::frames::iau_transform(e, t);
    const double w = m::deg_to_rad(120.0);
    const double rate = m::deg_to_rad(360.0) / k_day_s;
    // [W]_3 = rotation_z(-W); its derivative is -rotation_z'(-W) * W'.
    const Mat3 expected = Mat3::rotation_z(-w);
    const Mat3 expected_rate{{-std::sin(w) * rate, std::cos(w) * rate, 0.0, -std::cos(w) * rate,
                              -std::sin(w) * rate, 0.0, 0.0, 0.0, 0.0}};
    CHECK(m::max_abs_difference(x.rotation(), expected) < 1e-15);
    CHECK(m::max_abs_difference(x.rotation_rate_per_s(), expected_rate) < 1e-19);
}

TEST_CASE("the rotation rate equals the numerical derivative of the rotation", "[frames][iau]") {
    const IauRotationElements e = full_elements();
    for (const double t_s : {-3.0e9, 0.0, 4.5e8, 2.9e9}) {
        INFO(t_s);
        const Instant t = Instant::from_seconds_since_j2000(t_s);
        const double h = 1.0;
        const FrameTransform x = usim::frames::iau_transform(e, t);
        const Mat3 before = usim::frames::iau_transform(e, t.plus_seconds(-h)).rotation();
        const Mat3 after = usim::frames::iau_transform(e, t.plus_seconds(h)).rotation();
        const Mat3 numerical = (after + before * -1.0) * (1.0 / (2.0 * h));
        // W reaches about 2e5 rad here, so sin(W) and cos(W) resolve only about 3e-11; a wrong term would
        // be off by the size of the rate itself (about 7e-5 per second).
        CHECK(m::max_abs_difference(x.rotation_rate_per_s(), numerical) < 2e-11);
        // The rotation stays orthonormal.
        CHECK(m::max_abs_difference(x.rotation() * m::transpose(x.rotation()), Mat3::identity()) < 1e-15);
    }
}

TEST_CASE("elements given relative to ECLIPJ2000 are composed with the ecliptic rotation", "[frames][iau]") {
    IauRotationElements icrf = full_elements();
    IauRotationElements ecliptic = icrf;
    ecliptic.frame = FrameId::eclipj2000();
    const Instant t = Instant::from_seconds_since_j2000(7.7e8);
    const FrameTransform a = usim::frames::iau_transform(icrf, t);
    const FrameTransform b = usim::frames::iau_transform(ecliptic, t);
    const Mat3 to_ecliptic = usim::frames::icrf_to_eclipj2000();
    CHECK(b.rotation() == a.rotation() * to_ecliptic);
    CHECK(b.rotation_rate_per_s() == a.rotation_rate_per_s() * to_ecliptic);
}

TEST_CASE("the frame system resolves body-fixed frames from rotation elements", "[frames][iau]") {
    IauRotationElements first = full_elements();
    IauRotationElements duplicate = full_elements();
    duplicate.pm_deg = {0.0, 1.0};
    IauRotationElements other = full_elements();
    other.body = BodyId::naif(2000);
    other.pole_dec_deg = {-10.0};
    const FrameSystem frames({other, first, duplicate});
    REQUIRE(frames.find_rotation(BodyId::naif(1000)) != nullptr);
    CHECK(frames.find_rotation(BodyId::naif(1000))->pm_deg == first.pm_deg); // the first one wins
    CHECK(frames.find_rotation(BodyId::naif(3000)) == nullptr);

    const Instant t = Instant::from_seconds_since_j2000(1.0e8);
    const FrameId fixed = FrameId::body_fixed(BodyId::naif(1000));
    const FrameId fixed_other = FrameId::body_fixed(BodyId::naif(2000));
    const auto to_fixed = frames.transform(FrameId::icrf(), fixed, t);
    REQUIRE(to_fixed.has_value());
    CHECK(to_fixed.value().rotation() == usim::frames::iau_transform(first, t).rotation());

    // Between two body-fixed frames through the ICRF, and from ECLIPJ2000.
    const auto between = frames.transform(fixed, fixed_other, t);
    REQUIRE(between.has_value());
    const FrameTransform expected =
        usim::frames::iau_transform(first, t).inverse().then(usim::frames::iau_transform(other, t));
    CHECK(m::max_abs_difference(between.value().rotation(), expected.rotation()) < 1e-15);
    CHECK(m::max_abs_difference(between.value().rotation_rate_per_s(), expected.rotation_rate_per_s()) <
          1e-18);
    const auto from_ecliptic = frames.transform(FrameId::eclipj2000(), fixed, t);
    REQUIRE(from_ecliptic.has_value());
    CHECK(m::max_abs_difference(from_ecliptic.value().rotation(),
                                to_fixed.value().rotation() *
                                    m::transpose(usim::frames::icrf_to_eclipj2000())) < 1e-15);

    const auto missing = frames.transform(fixed, FrameId::body_fixed(BodyId::naif(3000)), t);
    REQUIRE_FALSE(missing.has_value());
    CHECK(missing.error().message.find("naif:3000") != std::string::npos);
}
