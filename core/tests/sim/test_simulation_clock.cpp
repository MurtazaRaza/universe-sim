// P4-01: the simulation clock (SPEC FR-SIM-05).

#include "usim/sim/simulation_clock.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cmath>
#include <limits>

using usim::ErrorCode;
using usim::ephemeris::TimeInterval;
using usim::sim::ClockLimit;
using usim::sim::SimulationClock;
using usim::time::Instant;

namespace {

Instant at(double s) {
    return Instant::from_seconds_since_j2000(s);
}

// Limits of [-1000, 1000] s, starting at 0.
SimulationClock make_clock() {
    return SimulationClock(TimeInterval{at(-1000.0), at(1000.0)}, at(0.0));
}

} // namespace

TEST_CASE("a new clock runs forward at rate 1 from its start", "[sim][clock]") {
    SimulationClock clock = make_clock();
    CHECK(clock.now() == at(0.0));
    CHECK(clock.rate() == 1.0);
    CHECK_FALSE(clock.paused());
    CHECK(clock.limit() == ClockLimit::none);
    REQUIRE(clock.advance(2.5).has_value());
    CHECK(clock.now() == at(2.5));
}

TEST_CASE("the start is clamped into the limits", "[sim][clock]") {
    const TimeInterval limits{at(-1000.0), at(1000.0)};
    CHECK(SimulationClock(limits, at(5000.0)).now() == at(1000.0));
    CHECK(SimulationClock(limits, at(-5000.0)).now() == at(-1000.0));
    CHECK(SimulationClock(limits, at(std::numeric_limits<double>::quiet_NaN())).now() == at(-1000.0));
}

TEST_CASE("the rate scales and signs the elapsed real time", "[sim][clock]") {
    SimulationClock clock = make_clock();
    REQUIRE(clock.set_rate(86.0).has_value());
    REQUIRE(clock.advance(2.0).has_value());
    CHECK(clock.now() == at(172.0));
    REQUIRE(clock.set_rate(-10.0).has_value());
    REQUIRE(clock.advance(1.5).has_value());
    CHECK(clock.now() == at(157.0));
    REQUIRE(clock.set_rate(0.0).has_value());
    REQUIRE(clock.advance(100.0).has_value());
    CHECK(clock.now() == at(157.0));
}

TEST_CASE("pause holds the time and keeps the rate", "[sim][clock]") {
    SimulationClock clock = make_clock();
    REQUIRE(clock.set_rate(3.0).has_value());
    clock.set_paused(true);
    REQUIRE(clock.advance(10.0).has_value());
    CHECK(clock.now() == at(0.0));
    CHECK(clock.rate() == 3.0);
    clock.set_paused(false);
    REQUIRE(clock.advance(10.0).has_value());
    CHECK(clock.now() == at(30.0));
}

TEST_CASE("the clock stops at its limits and records which one", "[sim][clock]") {
    SimulationClock clock = make_clock();
    REQUIRE(clock.set_rate(100.0).has_value());
    CHECK(clock.advance(9.0).value() == ClockLimit::none);
    CHECK(clock.now() == at(900.0));
    CHECK(clock.advance(5.0).value() == ClockLimit::end);
    CHECK(clock.now() == at(1000.0));
    CHECK(clock.limit() == ClockLimit::end);
    CHECK(clock.advance(5.0).value() == ClockLimit::end); // still pushing against it
    CHECK(clock.rate() == 100.0);                         // the rate is kept

    REQUIRE(clock.set_rate(-100.0).has_value());
    CHECK(clock.advance(1.0).value() == ClockLimit::none); // reversing moves away
    CHECK(clock.now() == at(900.0));
    CHECK(clock.advance(100.0).value() == ClockLimit::start);
    CHECK(clock.now() == at(-1000.0));
}

TEST_CASE("an overflowing step stops at the limit in its direction", "[sim][clock]") {
    SimulationClock clock = make_clock();
    REQUIRE(clock.set_rate(1.0e300).has_value());
    CHECK(clock.advance(1.0e300).value() == ClockLimit::end);
    CHECK(clock.now() == at(1000.0));
    REQUIRE(clock.set_rate(-1.0e300).has_value());
    CHECK(clock.advance(1.0e300).value() == ClockLimit::start);
    CHECK(clock.now() == at(-1000.0));
}

TEST_CASE("setting the time clamps it", "[sim][clock]") {
    SimulationClock clock = make_clock();
    CHECK(clock.set_time(at(500.0)).value() == ClockLimit::none);
    CHECK(clock.now() == at(500.0));
    CHECK(clock.set_time(at(1.0e6)).value() == ClockLimit::end);
    CHECK(clock.now() == at(1000.0));
    CHECK(clock.set_time(at(-1.0e6)).value() == ClockLimit::start);
    CHECK(clock.now() == at(-1000.0));
    CHECK(clock.set_time(at(1000.0)).value() == ClockLimit::none); // exactly at a limit is inside
}

TEST_CASE("invalid input is rejected and changes nothing", "[sim][clock]") {
    SimulationClock clock = make_clock();
    REQUIRE(clock.set_rate(2.0).has_value());
    REQUIRE(clock.advance(1.0).has_value());
    const double nan = std::numeric_limits<double>::quiet_NaN();
    const double inf = std::numeric_limits<double>::infinity();
    CHECK(clock.advance(-1.0).error().code == ErrorCode::invalid_argument);
    CHECK(clock.advance(nan).error().code == ErrorCode::invalid_argument);
    CHECK(clock.advance(inf).error().code == ErrorCode::invalid_argument);
    CHECK(clock.set_rate(nan).error().code == ErrorCode::invalid_argument);
    CHECK(clock.set_rate(-inf).error().code == ErrorCode::invalid_argument);
    CHECK(clock.set_time(at(nan)).error().code == ErrorCode::invalid_argument);
    CHECK(clock.now() == at(2.0));
    CHECK(clock.rate() == 2.0);
}

TEST_CASE("long runs keep full precision far from J2000", "[sim][clock]") {
    // A million steps of 1/60 s at rate 1, starting 100 years after J2000: the two-part time keeps the sum
    // exact to well under a microsecond, where one double of seconds would drift.
    const Instant start = Instant::from_days_and_seconds(36525, 0.0);
    SimulationClock clock(TimeInterval{start, start.plus_seconds(1.0e7)}, start);
    bool all_ok = true;
    for (int i = 0; i < 1'000'000; ++i) {
        all_ok = clock.advance(1.0 / 60.0).has_value() && all_ok;
    }
    REQUIRE(all_ok);
    CHECK(std::abs(clock.now().seconds_since(start) - 1.0e6 / 60.0) < 1.0e-6);
}
