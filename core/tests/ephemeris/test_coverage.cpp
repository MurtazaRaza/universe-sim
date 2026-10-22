#include "usim/ephemeris/coverage.hpp"

#include <catch2/catch_test_macros.hpp>

#include <limits>
#include <vector>

using usim::ephemeris::Coverage;
using usim::ephemeris::TimeInterval;
using usim::time::Instant;

namespace {

TimeInterval interval(double start_s, double end_s) {
    return {Instant::from_seconds_since_j2000(start_s), Instant::from_seconds_since_j2000(end_s)};
}

Instant at(double s) {
    return Instant::from_seconds_since_j2000(s);
}

} // namespace

TEST_CASE("coverage merges overlapping and touching intervals in any order", "[ephemeris][coverage]") {
    const std::vector<TimeInterval> parts{interval(50.0, 60.0), interval(0.0, 10.0),  interval(10.0, 20.0),
                                          interval(15.0, 18.0), interval(30.0, 40.0), interval(35.0, 45.0)};
    const Coverage c = Coverage::of(parts);
    const std::vector<TimeInterval> expected{interval(0.0, 20.0), interval(30.0, 45.0), interval(50.0, 60.0)};
    REQUIRE(std::vector<TimeInterval>(c.intervals().begin(), c.intervals().end()) == expected);
    REQUIRE(c.span() == interval(0.0, 60.0));
    CHECK_FALSE(c.empty());
}

TEST_CASE("coverage contains its closed intervals and nothing in the gaps", "[ephemeris][coverage]") {
    const std::vector<TimeInterval> parts{interval(0.0, 10.0), interval(20.0, 30.0)};
    const Coverage c = Coverage::of(parts);
    for (const double s : {0.0, 5.0, 10.0, 20.0, 30.0}) {
        CHECK(c.contains(at(s)));
    }
    for (const double s : {-1.0, 10.5, 19.999, 30.001, 1.0e9}) {
        CHECK_FALSE(c.contains(at(s)));
    }
    CHECK_FALSE(c.contains(at(std::numeric_limits<double>::quiet_NaN())));
    // Across a day boundary, where the two-part representation changes days.
    const std::vector<TimeInterval> days{interval(86000.0, 87000.0)};
    CHECK(Coverage::of(days).contains(at(86400.0)));
}

TEST_CASE("coverage ignores reversed and invalid intervals", "[ephemeris][coverage]") {
    const std::vector<TimeInterval> parts{interval(10.0, 5.0),
                                          interval(std::numeric_limits<double>::quiet_NaN(), 1.0)};
    const Coverage c = Coverage::of(parts);
    CHECK(c.empty());
    CHECK_FALSE(c.span().has_value());
    CHECK_FALSE(c.contains(at(7.0)));
    CHECK(Coverage{}.empty());
}

TEST_CASE("coverage intersection and union", "[ephemeris][coverage]") {
    const std::vector<TimeInterval> a_parts{interval(0.0, 10.0), interval(20.0, 30.0), interval(40.0, 50.0)};
    const std::vector<TimeInterval> b_parts{interval(5.0, 25.0), interval(30.0, 42.0)};
    const Coverage a = Coverage::of(a_parts);
    const Coverage b = Coverage::of(b_parts);

    const std::vector<TimeInterval> both{interval(5.0, 10.0), interval(20.0, 25.0), interval(30.0, 30.0),
                                         interval(40.0, 42.0)};
    const Coverage i = a.intersection(b);
    CHECK(std::vector<TimeInterval>(i.intervals().begin(), i.intervals().end()) == both);
    CHECK(b.intersection(a) == i);
    CHECK(a.intersection(Coverage{}).empty());
    CHECK(a.intersection(a) == a);

    const std::vector<TimeInterval> either{interval(0.0, 50.0)};
    const Coverage u = a.union_with(b);
    CHECK(std::vector<TimeInterval>(u.intervals().begin(), u.intervals().end()) == either);
    CHECK(a.union_with(Coverage{}) == a);
}
