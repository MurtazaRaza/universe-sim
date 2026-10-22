// P3-02 to P3-05: segment selection, chain resolution through the common ancestor, output frames and
// out-of-coverage handling, on synthetic segments and on the synthetic round-trip pack.

#include "ephemeris/ephemeris_test_support.hpp"
#include "fixtures/test_pack_expected.hpp"
#include "usim/data/data_pack.hpp"
#include "usim/ephemeris/ephemeris_chain.hpp"
#include "usim/frames/frame_system.hpp"
#include "usim/math/chebyshev.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <filesystem>
#include <limits>
#include <vector>

using Catch::Matchers::WithinAbs;
using usim::ErrorCode;
using usim::catalog::BodyId;
using usim::data::EphemerisSegment;
using usim::ephemeris::EphemerisChain;
using usim::frames::FrameId;
using usim::math::Vec3;
using usim::time::Instant;
namespace synth = usim::test::ephemeris;

namespace {

Instant at(double s) {
    return Instant::from_seconds_since_j2000(s);
}

BodyId naif(std::int64_t id) {
    return BodyId::naif(id);
}

// A tree with two branches: 1 and 3 orbit the root 0, 2 orbits 1 and 4 orbits 3. Positions are constant,
// with powers of two so that every sum is exact.
std::vector<EphemerisSegment> tree() {
    return {
        synth::constant_segment(1, 0, 1024.0, 0.0, 0.0),
        synth::constant_segment(2, 1, 0.0, 64.0, 0.0),
        synth::constant_segment(3, 0, 0.0, 0.0, 4096.0),
        synth::constant_segment(4, 3, 8.0, 16.0, 32.0),
    };
}

} // namespace

TEST_CASE("the chain finds the graph, its paths and its roots", "[ephemeris][chain]") {
    const auto segments = tree();
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const EphemerisChain& c = chain.value();
    CHECK(std::vector<BodyId>(c.bodies().begin(), c.bodies().end()) ==
          std::vector<BodyId>{naif(0), naif(1), naif(2), naif(3), naif(4)});
    CHECK(c.contains(naif(4)));
    CHECK_FALSE(c.contains(naif(5)));
    CHECK(c.parent(naif(2)) == naif(1));
    CHECK_FALSE(c.parent(naif(0)).has_value());
    CHECK_FALSE(c.parent(naif(5)).has_value());
    REQUIRE(c.path(naif(4)).has_value());
    CHECK(c.path(naif(4)).value() == std::vector<BodyId>{naif(4), naif(3), naif(0)});
    CHECK(c.path(naif(0)).value() == std::vector<BodyId>{naif(0)});
    CHECK(c.path(naif(5)).error().code == ErrorCode::not_found);
    CHECK(c.segments_of(naif(2)).size() == 1);
    CHECK(c.segments_of(naif(0)).empty());
}

TEST_CASE("states are summed up to the common ancestor", "[ephemeris][chain]") {
    const auto segments = tree();
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const auto state = [&](std::int64_t target, std::int64_t center) {
        const auto r = chain.value().state(naif(target), naif(center), FrameId::icrf(), at(0.0));
        REQUIRE(r.has_value());
        return r.value().position_km;
    };
    CHECK(state(2, 0) == Vec3{1024.0, 64.0, 0.0});
    CHECK(state(2, 1) == Vec3{0.0, 64.0, 0.0});
    CHECK(state(1, 2) == Vec3{0.0, -64.0, 0.0});
    CHECK(state(2, 4) == Vec3{1024.0 - 8.0, 64.0 - 16.0, -4096.0 - 32.0});
    CHECK(state(4, 2) == Vec3{8.0 - 1024.0, 16.0 - 64.0, 4096.0 + 32.0});
    CHECK(state(0, 4) == Vec3{-8.0, -16.0, -4096.0 - 32.0});
    CHECK(state(3, 3) == Vec3{});
    CHECK(state(0, 0) == Vec3{});
}

TEST_CASE("velocities are summed with the positions", "[ephemeris][chain]") {
    const std::vector<EphemerisSegment> segments{synth::linear_segment(1, 0, 0.5),
                                                 synth::linear_segment(2, 1, 0.25)};
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const auto r = chain.value().state(naif(2), naif(0), FrameId::icrf(), at(400.0));
    REQUIRE(r.has_value());
    CHECK_THAT(r.value().position_km.x, WithinAbs(0.75 * 400.0, 1e-12));
    CHECK_THAT(r.value().position_km.z, WithinAbs(2.25 * 400.0, 1e-12));
    CHECK_THAT(r.value().velocity_km_s.x, WithinAbs(0.75, 1e-14));
    CHECK_THAT(r.value().velocity_km_s.y, WithinAbs(1.5, 1e-14));
    CHECK_THAT(r.value().velocity_km_s.z, WithinAbs(2.25, 1e-14));
}

TEST_CASE("the later segment wins where segments of one target overlap", "[ephemeris][chain]") {
    // First segment over [-1000, 1000], a later one over [0, 2000] with a different position.
    const std::vector<EphemerisSegment> segments{
        synth::constant_segment(1, 0, 1.0, 0.0, 0.0, -1000.0, 500.0, 4),
        synth::constant_segment(1, 0, 2.0, 0.0, 0.0, 0.0, 500.0, 4),
    };
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const auto x_at = [&](double t) {
        const auto r = chain.value().state(naif(1), naif(0), FrameId::icrf(), at(t));
        REQUIRE(r.has_value());
        return r.value().position_km.x;
    };
    CHECK(x_at(-1000.0) == 1.0);
    CHECK(x_at(-0.001) == 1.0);
    CHECK(x_at(0.0) == 2.0);
    CHECK(x_at(1000.0) == 2.0);
    CHECK(x_at(2000.0) == 2.0);
    const auto own = chain.value().segments_of(naif(1));
    REQUIRE(own.size() == 2);
    CHECK(own[0]->data().records == segments[1].records); // highest precedence first
}

TEST_CASE("states outside the coverage are reported, never extrapolated", "[ephemeris][chain]") {
    // Body 1 is covered over [-1000, 1000]; body 2 relative to 1 only over [0, 500].
    const std::vector<EphemerisSegment> segments{
        synth::constant_segment(1, 0, 1.0, 0.0, 0.0, -1000.0, 500.0, 4),
        synth::constant_segment(2, 1, 0.0, 1.0, 0.0, 0.0, 500.0, 1),
    };
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const EphemerisChain& c = chain.value();

    const auto e = c.evaluate(naif(2), naif(0), FrameId::icrf(), at(-10.0));
    REQUIRE(e.error == ErrorCode::out_of_coverage);
    CHECK(e.failed_body == naif(2));
    CHECK(e.state.position_km == Vec3{});
    const auto e1 = c.evaluate(naif(2), naif(0), FrameId::icrf(), at(1500.0));
    CHECK(e1.error == ErrorCode::out_of_coverage);
    CHECK((e1.failed_body == naif(2) || e1.failed_body == naif(1)));
    CHECK(c.evaluate(naif(1), naif(0), FrameId::icrf(), at(-10.0)).ok());
    // Body 2 relative to 1 does not need body 1's segment.
    CHECK(c.evaluate(naif(2), naif(1), FrameId::icrf(), at(250.0)).ok());

    const auto r = c.state(naif(2), naif(0), FrameId::icrf(), at(-10.0));
    REQUIRE_FALSE(r.has_value());
    CHECK(r.error().code == ErrorCode::out_of_coverage);
    CHECK(r.error().message.find("naif:2") != std::string::npos);
    CHECK(c.state(naif(1), naif(0), FrameId::icrf(), at(std::numeric_limits<double>::quiet_NaN()))
              .error()
              .code == ErrorCode::invalid_argument);
    CHECK(c.evaluate(naif(1), naif(1), FrameId::icrf(), at(std::numeric_limits<double>::quiet_NaN())).error ==
          ErrorCode::out_of_coverage);
}

TEST_CASE("coverage per body and per pair is the intersection along the paths", "[ephemeris][chain]") {
    const std::vector<EphemerisSegment> segments{
        synth::constant_segment(1, 0, 1.0, 0.0, 0.0, -1000.0, 500.0, 4), // [-1000, 1000]
        synth::constant_segment(2, 1, 0.0, 1.0, 0.0, 0.0, 500.0, 4),     // [0, 2000]
        synth::constant_segment(3, 0, 0.0, 0.0, 1.0, 1500.0, 500.0, 2),  // [1500, 2500]
    };
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const EphemerisChain& c = chain.value();
    const auto span = [](const usim::Result<usim::ephemeris::Coverage>& coverage) {
        REQUIRE(coverage.has_value());
        REQUIRE(coverage.value().intervals().size() == 1);
        return std::pair{coverage.value().intervals()[0].start.seconds_since_j2000(),
                         coverage.value().intervals()[0].end.seconds_since_j2000()};
    };
    CHECK(span(c.coverage(naif(1))) == std::pair{-1000.0, 1000.0});
    CHECK(span(c.coverage(naif(2))) == std::pair{0.0, 1000.0});
    CHECK(span(c.coverage(naif(0))) == std::pair{-1000.0, 2500.0}); // a root: the span of all segments
    CHECK(span(c.coverage(naif(2), naif(1))) == std::pair{0.0, 2000.0});
    CHECK(span(c.coverage(naif(1), naif(2))) == std::pair{0.0, 2000.0});
    CHECK(c.coverage(naif(2), naif(3)).value().empty());
    CHECK(c.coverage(naif(9)).error().code == ErrorCode::not_found);
}

TEST_CASE("unknown and unconnected bodies are not found", "[ephemeris][chain]") {
    const std::vector<EphemerisSegment> segments{synth::constant_segment(1, 0, 1.0, 0.0, 0.0),
                                                 synth::constant_segment(11, 10, 1.0, 0.0, 0.0)};
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const EphemerisChain& c = chain.value();
    const auto unknown = c.evaluate(naif(1), naif(99), FrameId::icrf(), at(0.0));
    CHECK(unknown.error == ErrorCode::not_found);
    CHECK(unknown.failed_body == naif(99));
    const auto apart = c.evaluate(naif(1), naif(11), FrameId::icrf(), at(0.0));
    CHECK(apart.error == ErrorCode::not_found);
    CHECK_FALSE(apart.failed_body.is_valid());
    CHECK(c.state(naif(1), naif(11), FrameId::icrf(), at(0.0)).error().message.find("connects") !=
          std::string::npos);
    CHECK(c.state(naif(1), naif(99), FrameId::icrf(), at(0.0)).error().message.find("naif:99") !=
          std::string::npos);
    CHECK(c.coverage(naif(1), naif(11)).error().code == ErrorCode::not_found);
}

TEST_CASE("states are given in the ICRF or rotated to ECLIPJ2000", "[ephemeris][chain]") {
    const std::vector<EphemerisSegment> segments{synth::linear_segment(1, 0, 0.5)};
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const auto icrf = chain.value().state(naif(1), naif(0), FrameId::icrf(), at(250.0));
    const auto ecliptic = chain.value().state(naif(1), naif(0), FrameId::eclipj2000(), at(250.0));
    REQUIRE(icrf.has_value());
    REQUIRE(ecliptic.has_value());
    const usim::math::Mat3 r = usim::frames::icrf_to_eclipj2000();
    CHECK(ecliptic.value().position_km == r * icrf.value().position_km);
    CHECK(ecliptic.value().velocity_km_s == r * icrf.value().velocity_km_s);

    const auto fixed = chain.value().state(naif(1), naif(0), FrameId::body_fixed(naif(1)), at(250.0));
    REQUIRE_FALSE(fixed.has_value());
    CHECK(fixed.error().code == ErrorCode::unsupported);
}

TEST_CASE("segments given in ECLIPJ2000 are rotated into the ICRF", "[ephemeris][chain]") {
    auto ecliptic_segment = synth::constant_segment(1, 0, 0.0, 512.0, 0.0);
    ecliptic_segment.frame = FrameId::eclipj2000();
    const std::vector<EphemerisSegment> segments{ecliptic_segment};
    const auto chain = EphemerisChain::create(segments);
    REQUIRE(chain.has_value());
    const auto in_ecliptic = chain.value().state(naif(1), naif(0), FrameId::eclipj2000(), at(0.0));
    const auto in_icrf = chain.value().state(naif(1), naif(0), FrameId::icrf(), at(0.0));
    REQUIRE(in_ecliptic.has_value());
    REQUIRE(in_icrf.has_value());
    const Vec3 expected = usim::math::transpose(usim::frames::icrf_to_eclipj2000()) * Vec3{0.0, 512.0, 0.0};
    CHECK(in_icrf.value().position_km == expected);
    CHECK_THAT(in_ecliptic.value().position_km.y, WithinAbs(512.0, 1e-12));
    CHECK_THAT(in_ecliptic.value().position_km.x, WithinAbs(0.0, 1e-12));
    CHECK_THAT(in_ecliptic.value().position_km.z, WithinAbs(0.0, 1e-12));
}

TEST_CASE("malformed graphs are rejected", "[ephemeris][chain]") {
    SECTION("a segment relative to itself") {
        const std::vector<EphemerisSegment> segments{synth::constant_segment(1, 1, 1.0, 0.0, 0.0)};
        CHECK(EphemerisChain::create(segments).error().code == ErrorCode::data_format);
    }
    SECTION("a cycle") {
        const std::vector<EphemerisSegment> segments{synth::constant_segment(1, 2, 1.0, 0.0, 0.0),
                                                     synth::constant_segment(2, 3, 1.0, 0.0, 0.0),
                                                     synth::constant_segment(3, 1, 1.0, 0.0, 0.0)};
        CHECK(EphemerisChain::create(segments).error().code == ErrorCode::data_format);
    }
    SECTION("one target with two centers") {
        const std::vector<EphemerisSegment> segments{synth::constant_segment(1, 0, 1.0, 0.0, 0.0),
                                                     synth::constant_segment(1, 5, 1.0, 0.0, 0.0)};
        const auto r = EphemerisChain::create(segments);
        REQUIRE_FALSE(r.has_value());
        CHECK(r.error().code == ErrorCode::unsupported);
    }
    SECTION("a malformed record") {
        auto bad = synth::constant_segment(1, 0, 1.0, 0.0, 0.0);
        bad.records[1] = -1.0;
        const std::vector<EphemerisSegment> segments{bad};
        CHECK(EphemerisChain::create(segments).error().code == ErrorCode::data_format);
    }
    SECTION("no segments gives an empty graph") {
        const auto r = EphemerisChain::create(std::span<const EphemerisSegment>{});
        REQUIRE(r.has_value());
        CHECK(r.value().bodies().empty());
    }
}

TEST_CASE("the chain of the round-trip pack sums its synthetic segments", "[ephemeris][chain]") {
    namespace expected = usim::test::pack;
    const auto pack = usim::data::DataPack::load_file(std::filesystem::path(USIM_TEST_FIXTURES_DIR) /
                                                      expected::k_file_name);
    REQUIRE(pack.has_value());
    const auto chain = EphemerisChain::create(pack.value());
    REQUIRE(chain.has_value());
    REQUIRE(chain.value().path(naif(1002)).value() ==
            std::vector<BodyId>{naif(1002), naif(1001), naif(1000), naif(0)});

    // Each segment evaluated by hand from the generator's records, then summed.
    const auto segment_state = [](const expected::SegmentRow& row, double t) {
        const auto index = static_cast<std::size_t>(std::min<double>(
            std::floor((t - row.init_tdb_s) / row.interval_s), static_cast<double>(row.record_count - 1)));
        const double* record = row.records.data() + index * row.record_size;
        const double s = (t - record[0]) / record[1];
        const std::size_t n = (row.record_size - 2) / (row.spk_type == 3 ? 6 : 3);
        Vec3 p;
        double* out = &p.x;
        for (std::size_t c = 0; c < 3; ++c) {
            out[c] = usim::math::chebyshev(std::span<const double>(record + 2 + c * n, n), s);
        }
        return p;
    };
    for (const double t : {3600.0, 40000.0, 86400.0}) {
        INFO(t);
        const Vec3 expected_position = segment_state(expected::k_segments[0], t) +
                                       segment_state(expected::k_segments[1], t) +
                                       segment_state(expected::k_segments[2], t);
        const auto r = chain.value().state(naif(1002), naif(0), FrameId::icrf(), at(t));
        REQUIRE(r.has_value());
        CHECK_THAT(r.value().position_km.x, WithinAbs(expected_position.x, 1e-12));
        CHECK_THAT(r.value().position_km.y, WithinAbs(expected_position.y, 1e-12));
        CHECK_THAT(r.value().position_km.z, WithinAbs(expected_position.z, 1e-12));
    }
    // 1001 is covered only from 3600 s, so 1002 relative to the root is not covered before.
    CHECK(chain.value().state(naif(1002), naif(0), FrameId::icrf(), at(0.0)).error().code ==
          ErrorCode::out_of_coverage);
}

TEST_CASE("approximated bodies are placed at their proxy and flagged", "[ephemeris][chain]") {
    // Root 0, barycenter 4 relative to 0, and body 499 placed at 4; body 3 relative to 0 for contrast.
    const std::vector<EphemerisSegment> segments{synth::linear_segment(4, 0, 0.5),
                                                 synth::constant_segment(3, 0, 8.0, 16.0, 32.0)};
    const std::vector<usim::data::PositionApproximation> approximations{{naif(499), naif(4), "synthetic"}};
    const auto chain = EphemerisChain::create(segments, approximations);
    REQUIRE(chain.has_value());
    const EphemerisChain& c = chain.value();

    CHECK(c.path(naif(499)).value() == std::vector<BodyId>{naif(499), naif(4), naif(0)});
    CHECK(c.segments_of(naif(499)).empty());
    REQUIRE(c.approximation(naif(499)) != nullptr);
    CHECK(c.approximation(naif(499))->reason == "synthetic");
    CHECK(c.approximation(naif(4)) == nullptr);

    const Instant t = at(400.0);
    const auto approximate = c.evaluate(naif(499), naif(0), FrameId::icrf(), t);
    const auto proxy = c.evaluate(naif(4), naif(0), FrameId::icrf(), t);
    REQUIRE(approximate.ok());
    REQUIRE(proxy.ok());
    CHECK(approximate.approximate);
    CHECK_FALSE(proxy.approximate);
    CHECK(approximate.state.position_km == proxy.state.position_km);
    CHECK(approximate.state.velocity_km_s == proxy.state.velocity_km_s);

    // Relative to the proxy the state is zero, still approximate; from a third body it is flagged too.
    const auto at_proxy = c.evaluate(naif(499), naif(4), FrameId::icrf(), t);
    REQUIRE(at_proxy.ok());
    CHECK(at_proxy.state.position_km == Vec3{});
    CHECK(at_proxy.approximate);
    CHECK(c.evaluate(naif(3), naif(499), FrameId::icrf(), t).approximate);
    CHECK_FALSE(c.evaluate(naif(3), naif(4), FrameId::icrf(), t).approximate);
    CHECK_FALSE(c.evaluate(naif(499), naif(499), FrameId::icrf(), t).approximate);

    CHECK(c.is_approximate(naif(499), naif(0)));
    CHECK(c.is_approximate(naif(0), naif(499)));
    CHECK(c.is_approximate(naif(3), naif(499)));
    CHECK_FALSE(c.is_approximate(naif(3), naif(4)));
    CHECK_FALSE(c.is_approximate(naif(499), naif(499)));
    CHECK_FALSE(c.is_approximate(naif(99), naif(0)));

    // Coverage follows the proxy; outside it the approximated body is out of coverage too.
    CHECK(c.coverage(naif(499)).value() == c.coverage(naif(4)).value());
    const auto outside = c.evaluate(naif(499), naif(4), FrameId::icrf(), at(1.0e6));
    CHECK(outside.error == ErrorCode::out_of_coverage);
}

TEST_CASE("inconsistent approximations are rejected", "[ephemeris][chain]") {
    const std::vector<EphemerisSegment> segments{synth::constant_segment(4, 0, 1.0, 0.0, 0.0),
                                                 synth::constant_segment(499, 4, 1.0, 0.0, 0.0)};
    using Approximations = std::vector<usim::data::PositionApproximation>;
    SECTION("the body has segments") {
        const Approximations a{{naif(499), naif(4), "x"}};
        CHECK(EphemerisChain::create(segments, a).error().code == ErrorCode::data_format);
    }
    SECTION("the proxy has no ephemeris") {
        const Approximations a{{naif(599), naif(5), "x"}};
        CHECK(EphemerisChain::create(segments, a).error().code == ErrorCode::data_format);
    }
    SECTION("a body twice") {
        const Approximations a{{naif(599), naif(4), "x"}, {naif(599), naif(4), "y"}};
        CHECK(EphemerisChain::create(segments, a).error().code == ErrorCode::data_format);
    }
}
