// P4-04: orbit path sampling on synthetic orbits (made-up values, not physical data): a circle and a
// Kepler ellipse, fitted into Chebyshev records.

#include "ephemeris/ephemeris_test_support.hpp"
#include "usim/sim/orbit_path.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <algorithm>
#include <cmath>
#include <numbers>
#include <vector>

using Catch::Matchers::WithinRel;
using usim::ErrorCode;
using usim::catalog::BodyId;
using usim::data::EphemerisSegment;
using usim::ephemeris::EphemerisChain;
using usim::ephemeris::TimeInterval;
using usim::frames::FrameId;
using usim::math::Vec3;
using usim::sim::OrbitPathCache;
using usim::time::Instant;
namespace synth = usim::test::ephemeris;

namespace {

constexpr double k_period_s = 86400.0;
constexpr double k_two_pi = 2.0 * std::numbers::pi;

Instant at(double s) {
    return Instant::from_seconds_since_j2000(s);
}

// A circle of radius 1000 km in the ICRF XY plane, one turn per k_period_s.
Vec3 circle(double t) {
    const double phase = k_two_pi * t / k_period_s;
    return {1000.0 * std::cos(phase), 1000.0 * std::sin(phase), 0.0};
}

// A Kepler ellipse with eccentricity 0.6 and semi-major axis 1000 km, periapsis at t = 0.
Vec3 ellipse(double t) {
    const double e = 0.6;
    const double mean_anomaly = k_two_pi * t / k_period_s;
    double anomaly = mean_anomaly;
    for (int i = 0; i < 50; ++i) {
        anomaly -= (anomaly - e * std::sin(anomaly) - mean_anomaly) / (1.0 - e * std::cos(anomaly));
    }
    return {1000.0 * (std::cos(anomaly) - e), 1000.0 * std::sqrt(1.0 - e * e) * std::sin(anomaly), 0.0};
}

// Five periods either side of J2000, 40 records per period.
EphemerisSegment orbit_segment(Vec3 (*f)(double)) {
    return synth::fitted_segment(1, 0, f, -5.0 * k_period_s, k_period_s / 40.0, 400, 16);
}

TimeInterval whole(const EphemerisSegment& s) {
    return {at(s.start_tdb_s), at(s.end_tdb_s)};
}

double angle(const Vec3& a, const Vec3& b) {
    return std::atan2(usim::math::norm(usim::math::cross(a, b)), usim::math::dot(a, b));
}

} // namespace

TEST_CASE("a revolution of a circular orbit spans one period, centered on the instant", "[sim][orbit]") {
    const std::vector<EphemerisSegment> segments{orbit_segment(circle)};
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const auto path = usim::sim::sample_revolution(chain.value(), BodyId::naif(1), BodyId::naif(0),
                                                   FrameId::icrf(), at(1000.0), whole(segments[0]));
    REQUIRE(path.has_value());
    const auto& p = path.value();
    CHECK(p.full_revolution);
    CHECK_FALSE(p.approximate);
    CHECK_THAT(p.end.seconds_since(p.start), WithinRel(k_period_s, 1e-6));
    CHECK_THAT(at(1000.0).seconds_since(p.start), WithinRel(k_period_s / 2.0, 1e-6));
    REQUIRE(p.points.size() >= 720); // at most 0.5 degrees apart
    CHECK(p.points.front().t == p.start);
    CHECK(p.points.back().t == p.end);
    double largest = 0.0;
    for (std::size_t i = 0; i < p.points.size(); ++i) {
        CHECK_THAT(usim::math::norm(p.points[i].position_km), WithinRel(1000.0, 1e-9));
        if (i > 0) {
            CHECK(p.points[i - 1].t < p.points[i].t);
            largest = std::max(largest, angle(p.points[i - 1].position_km, p.points[i].position_km));
        }
    }
    CHECK(largest <= usim::math::deg_to_rad(0.5) + 1e-12);
    // It closes: the last point is where the first was.
    CHECK(usim::math::distance(p.points.front().position_km, p.points.back().position_km) < 1e-3);
}

TEST_CASE("an eccentric orbit gets denser samples near periapsis", "[sim][orbit]") {
    const std::vector<EphemerisSegment> segments{orbit_segment(ellipse)};
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const auto path = usim::sim::sample_revolution(chain.value(), BodyId::naif(1), BodyId::naif(0),
                                                   FrameId::icrf(), at(0.0), whole(segments[0]));
    REQUIRE(path.has_value());
    const auto& points = path.value().points;
    CHECK(path.value().full_revolution);
    CHECK_THAT(path.value().end.seconds_since(path.value().start), WithinRel(k_period_s, 1e-6));
    // Time between samples near periapsis (distance 400 km) and near apoapsis (1600 km).
    double near_periapsis = k_period_s;
    double near_apoapsis = 0.0;
    for (std::size_t i = 1; i < points.size(); ++i) {
        const double gap = points[i].t.seconds_since(points[i - 1].t);
        const double r = usim::math::norm(points[i].position_km);
        if (r < 500.0) {
            near_periapsis = std::min(near_periapsis, gap);
        }
        if (r > 1500.0) {
            near_apoapsis = std::max(near_apoapsis, gap);
        }
    }
    CHECK(near_apoapsis > 8.0 * near_periapsis);
}

TEST_CASE("coverage shorter than a revolution gives a partial path", "[sim][orbit]") {
    const std::vector<EphemerisSegment> segments{orbit_segment(circle)};
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const TimeInterval short_coverage{at(-0.2 * k_period_s), at(0.3 * k_period_s)};
    const auto path = usim::sim::sample_revolution(chain.value(), BodyId::naif(1), BodyId::naif(0),
                                                   FrameId::icrf(), at(0.0), short_coverage);
    REQUIRE(path.has_value());
    CHECK_FALSE(path.value().full_revolution);
    CHECK(path.value().start == short_coverage.start);
    CHECK(path.value().end == short_coverage.end);

    // Near the end of the coverage the window shifts back to stay inside.
    const TimeInterval all = whole(segments[0]);
    const auto late =
        usim::sim::sample_revolution(chain.value(), BodyId::naif(1), BodyId::naif(0), FrameId::icrf(),
                                     all.end.plus_seconds(-0.1 * k_period_s), all);
    REQUIRE(late.has_value());
    CHECK(late.value().full_revolution);
    CHECK(late.value().end == all.end);
    CHECK_THAT(late.value().end.seconds_since(late.value().start), WithinRel(k_period_s, 1e-6));
}

TEST_CASE("a path over a given window respects the options", "[sim][orbit]") {
    const std::vector<EphemerisSegment> segments{orbit_segment(circle)};
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    usim::sim::OrbitPathOptions coarse;
    coarse.max_angle_rad = usim::math::deg_to_rad(10.0);
    coarse.max_chord_error = 1.0;
    coarse.initial_intervals = 4;
    const auto path = usim::sim::sample_path(chain.value(), BodyId::naif(1), BodyId::naif(0),
                                             FrameId::eclipj2000(), at(0.0), at(k_period_s / 2.0), coarse);
    REQUIRE(path.has_value());
    CHECK(path.value().points.size() ==
          33); // 180 degrees in 16 steps of 5.6 degrees, after splitting 45-degree ones
    CHECK(path.value().frame == FrameId::eclipj2000());

    usim::sim::OrbitPathOptions capped;
    capped.max_points = 50;
    const auto limited = usim::sim::sample_path(chain.value(), BodyId::naif(1), BodyId::naif(0),
                                                FrameId::icrf(), at(0.0), at(k_period_s), capped);
    REQUIRE(limited.has_value());
    CHECK(limited.value().points.size() == 50); // refined up to the cap, not past it
    CHECK(limited.value().points.back().t == at(k_period_s));
}

TEST_CASE("invalid orbit path requests are rejected", "[sim][orbit]") {
    const std::vector<EphemerisSegment> segments{orbit_segment(circle)};
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const auto& c = chain.value();
    const BodyId body = BodyId::naif(1);
    const BodyId center = BodyId::naif(0);
    CHECK(usim::sim::sample_path(c, body, body, FrameId::icrf(), at(0.0), at(1.0)).error().code ==
          ErrorCode::invalid_argument);
    CHECK(usim::sim::sample_path(c, body, center, FrameId::body_fixed(body), at(0.0), at(1.0)).error().code ==
          ErrorCode::invalid_argument);
    CHECK(usim::sim::sample_path(c, body, center, FrameId::icrf(), at(1.0), at(1.0)).error().code ==
          ErrorCode::invalid_argument);
    usim::sim::OrbitPathOptions bad;
    bad.max_angle_rad = 0.0;
    CHECK(usim::sim::sample_path(c, body, center, FrameId::icrf(), at(0.0), at(1.0), bad).error().code ==
          ErrorCode::invalid_argument);
    CHECK(usim::sim::sample_path(c, body, center, FrameId::icrf(), at(0.0), at(1.0e9)).error().code ==
          ErrorCode::out_of_coverage);
    CHECK(usim::sim::sample_revolution(c, body, center, FrameId::icrf(), at(1.0e9), whole(segments[0]))
              .error()
              .code == ErrorCode::out_of_coverage);
}

TEST_CASE("the cache samples again only after a large time jump", "[sim][orbit]") {
    const std::vector<EphemerisSegment> segments{orbit_segment(circle)};
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    OrbitPathCache cache(chain.value(), whole(segments[0]));
    const BodyId body = BodyId::naif(1);
    const BodyId center = BodyId::naif(0);

    const auto first = cache.revolution(body, center, FrameId::icrf(), at(0.0));
    REQUIRE(first.has_value());
    CHECK(cache.samplings() == 1);
    const auto again = cache.revolution(body, center, FrameId::icrf(), at(0.01 * k_period_s)); // within 2 %
    REQUIRE(again.has_value());
    CHECK(again.value() == first.value());
    CHECK(cache.samplings() == 1);
    const auto other_frame = cache.revolution(body, center, FrameId::eclipj2000(), at(0.0));
    REQUIRE(other_frame.has_value());
    CHECK(cache.samplings() == 2);
    CHECK(first.value()->frame == FrameId::icrf()); // still valid after adding an entry

    const auto jumped = cache.revolution(body, center, FrameId::icrf(), at(2.0 * k_period_s));
    REQUIRE(jumped.has_value());
    CHECK(cache.samplings() == 3);
    CHECK_THAT(at(2.0 * k_period_s).seconds_since(jumped.value()->start), WithinRel(k_period_s / 2.0, 1e-6));
    cache.clear();
    REQUIRE(cache.revolution(body, center, FrameId::icrf(), at(2.0 * k_period_s)).has_value());
    CHECK(cache.samplings() == 4);
}
