// P3-01 and P3-02: evaluation of SPK type 2 and type 3 records and record lookup, on synthetic segments
// (the real kernel is compared with CSPICE in the validation tests, VAL-01).

#include "ephemeris/ephemeris_test_support.hpp"
#include "usim/ephemeris/chebyshev_segment.hpp"
#include "usim/math/chebyshev.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>
#include <limits>

using Catch::Matchers::WithinAbs;
using Catch::Matchers::WithinRel;
using usim::ErrorCode;
using usim::ephemeris::ChebyshevSegment;
using usim::time::Instant;
namespace synth = usim::test::ephemeris;

namespace {

Instant at(double s) {
    return Instant::from_seconds_since_j2000(s);
}

} // namespace

TEST_CASE("type 2: position from the polynomials, velocity from their derivative", "[ephemeris][segment]") {
    // Degree 3 with distinct coefficients per record and component.
    const auto data = synth::make_segment(
        1000, 0, 2, 3, -2000.0, 1000.0, 4, [](std::uint32_t r, std::uint32_t c, std::uint32_t k) {
            return 1.0 + 0.5 * static_cast<double>(r) - 0.25 * static_cast<double>(c) +
                   0.125 * static_cast<double>(k * k);
        });
    const auto segment = ChebyshevSegment::create(data);
    REQUIRE(segment.has_value());
    for (const double t : {-2000.0, -1999.5, -1500.0, -1000.0, 0.0, 250.0, 1234.5, 2000.0}) {
        INFO(t);
        const auto state = segment.value().state(at(t));
        REQUIRE(state.has_value());
        const std::size_t r = segment.value().record_index(at(t));
        const double* record = data.records.data() + r * data.record_size;
        const double s = (t - record[0]) / record[1];
        const double* p = &state->position_km.x;
        const double* v = &state->velocity_km_s.x;
        for (std::size_t c = 0; c < 3; ++c) {
            const std::span<const double> coefficients(record + 2 + c * 4, 4);
            const auto expected = usim::math::chebyshev_with_derivative(coefficients, s);
            CHECK(p[c] == expected.value);
            CHECK(v[c] == expected.derivative / record[1]);
        }
    }
}

TEST_CASE("type 2: a linear motion is reproduced across records", "[ephemeris][segment]") {
    const auto data = synth::linear_segment(1000, 0, 0.75);
    const auto segment = ChebyshevSegment::create(data);
    REQUIRE(segment.has_value());
    for (const double t : {-1000.0, -999.0, -500.0, -0.25, 0.0, 333.0, 999.999, 1000.0}) {
        INFO(t);
        const auto state = segment.value().state(at(t));
        REQUIRE(state.has_value());
        CHECK_THAT(state->position_km.x, WithinAbs(0.75 * t, 1e-12));
        CHECK_THAT(state->position_km.y, WithinAbs(1.5 * t, 1e-12));
        CHECK_THAT(state->position_km.z, WithinAbs(2.25 * t, 1e-12));
        CHECK_THAT(state->velocity_km_s.x, WithinRel(0.75, 1e-14));
        CHECK_THAT(state->velocity_km_s.y, WithinRel(1.5, 1e-14));
        CHECK_THAT(state->velocity_km_s.z, WithinRel(2.25, 1e-14));
    }
}

TEST_CASE("type 3: velocity comes from its own polynomials", "[ephemeris][segment]") {
    const auto data = synth::make_segment(
        1001, 1000, 3, 2, 0.0, 600.0, 2, [](std::uint32_t r, std::uint32_t c, std::uint32_t k) {
            return static_cast<double>(10 * c + k) + 100.0 * static_cast<double>(r);
        });
    const auto segment = ChebyshevSegment::create(data);
    REQUIRE(segment.has_value());
    for (const double t : {0.0, 100.0, 599.0, 600.0, 1000.0, 1200.0}) {
        INFO(t);
        const auto state = segment.value().state(at(t));
        REQUIRE(state.has_value());
        const std::size_t r = segment.value().record_index(at(t));
        const double* record = data.records.data() + r * data.record_size;
        const double s = (t - record[0]) / record[1];
        const double* p = &state->position_km.x;
        const double* v = &state->velocity_km_s.x;
        for (std::size_t c = 0; c < 3; ++c) {
            CHECK(p[c] == usim::math::chebyshev(std::span<const double>(record + 2 + c * 3, 3), s));
            CHECK(v[c] == usim::math::chebyshev(std::span<const double>(record + 2 + (c + 3) * 3, 3), s));
        }
    }
}

TEST_CASE("record lookup indexes directly from the record interval", "[ephemeris][segment]") {
    const auto data = synth::constant_segment(1000, 0, 1.0, 2.0, 3.0, -1000.0, 500.0, 4);
    const auto segment = ChebyshevSegment::create(data);
    REQUIRE(segment.has_value());
    const ChebyshevSegment& s = segment.value();
    CHECK(s.record_index(at(-1000.0)) == 0);
    CHECK(s.record_index(at(-500.001)) == 0);
    CHECK(s.record_index(at(-500.0)) == 1);
    CHECK(s.record_index(at(0.0)) == 2);
    CHECK(s.record_index(at(499.999)) == 2);
    CHECK(s.record_index(at(500.0)) == 3);
    CHECK(s.record_index(at(1000.0)) == 3);  // the final instant uses the last record, as in SPICE
    CHECK(s.record_index(at(-5000.0)) == 0); // clamped
    CHECK(s.record_index(at(5000.0)) == 3);
}

TEST_CASE("record lookup is exact for instants given in two parts far from J2000", "[ephemeris][segment]") {
    // Records of one day starting 10 000 days after J2000. An instant a nanosecond before a boundary must
    // still use the earlier record, which one double of TDB seconds (resolution about 0.1 us there) cannot
    // even represent.
    const double init = 10000.0 * 86400.0;
    const auto data = synth::constant_segment(1000, 0, 1.0, 2.0, 3.0, init, 86400.0, 3);
    const auto segment = ChebyshevSegment::create(data);
    REQUIRE(segment.has_value());
    const Instant before = Instant::from_days_and_seconds(10001, 86400.0 - 1.0e-9);
    const Instant boundary = Instant::from_days_and_seconds(10002, 0.0);
    CHECK(segment.value().record_index(before) == 1);
    CHECK(segment.value().record_index(boundary) == 2);
}

TEST_CASE("a segment never extrapolates outside its coverage", "[ephemeris][segment]") {
    auto data = synth::linear_segment(1000, 0, 1.0);
    data.start_tdb_s = -900.0; // coverage narrower than the records
    data.end_tdb_s = 900.0;
    const auto segment = ChebyshevSegment::create(data);
    REQUIRE(segment.has_value());
    const ChebyshevSegment& s = segment.value();
    CHECK(s.state(at(-900.0)).has_value());
    CHECK(s.state(at(900.0)).has_value());
    CHECK_FALSE(s.state(at(-900.001)).has_value());
    CHECK_FALSE(s.state(at(900.001)).has_value());
    CHECK_FALSE(s.state(at(-1000.0)).has_value());
    CHECK_FALSE(s.state(at(std::numeric_limits<double>::quiet_NaN())).has_value());
    CHECK_FALSE(s.state(at(std::numeric_limits<double>::infinity())).has_value());
    CHECK(s.coverage().start == at(-900.0));
    CHECK(s.coverage().end == at(900.0));
}

TEST_CASE("segments whose records do not span their intervals are rejected", "[ephemeris][segment]") {
    SECTION("non-positive radius") {
        auto data = synth::constant_segment(1000, 0, 1.0, 2.0, 3.0);
        data.records[data.record_size + 1] = 0.0;
        const auto r = ChebyshevSegment::create(data);
        REQUIRE_FALSE(r.has_value());
        CHECK(r.error().code == ErrorCode::data_format);
        CHECK(r.error().message.find("record 1") != std::string::npos);
    }
    SECTION("record interval shifted away from the record's slot") {
        auto data = synth::constant_segment(1000, 0, 1.0, 2.0, 3.0);
        data.records[2 * data.record_size] += 10.0; // MID of record 2
        const auto r = ChebyshevSegment::create(data);
        REQUIRE_FALSE(r.has_value());
        CHECK(r.error().code == ErrorCode::data_format);
    }
    SECTION("record data shorter than the layout") {
        auto data = synth::constant_segment(1000, 0, 1.0, 2.0, 3.0);
        data.records.pop_back();
        const auto r = ChebyshevSegment::create(data);
        REQUIRE_FALSE(r.has_value());
        CHECK(r.error().code == ErrorCode::data_format);
    }
}
