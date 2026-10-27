// Binary PCK orientation models (P6-05, P6-06): record evaluation, segment precedence and coverage, segments
// relative to ECLIPJ2000, the frame kernel offset and models without one, and their use by the frame system.
// The angles and offsets here are synthetic, not physical data; agreement with CSPICE on the real lunar and
// Earth kernels is VAL-04 (validation tests).

#include "usim/frames/euler.hpp"
#include "usim/frames/frame_system.hpp"
#include "usim/frames/pck_orientation.hpp"
#include "usim/math/angle.hpp"
#include "usim/math/mat3.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cmath>
#include <vector>

using usim::ErrorCode;
using usim::catalog::BodyId;
using usim::frames::AngleUnit;
using usim::frames::FrameId;
using usim::frames::FrameKind;
using usim::frames::FrameSystem;
using usim::frames::PckOrientation;
using usim::frames::PckSegment;
using usim::math::Mat3;
using usim::time::Instant;
namespace frames = usim::frames;
namespace m = usim::math;

namespace {

// One segment of `count` records of `interval_s` from `init_s`, each a degree 1 polynomial per angle:
// angle k = base[k] + slope[k] * s with s in [-1, 1] over the record (so its rate is slope[k] / RADIUS).
PckSegment linear_segment(double init_s, double interval_s, std::uint32_t count, std::array<double, 3> base,
                          std::array<double, 3> slope) {
    PckSegment segment;
    segment.init_tdb_s = init_s;
    segment.start_tdb_s = init_s;
    segment.end_tdb_s = init_s + interval_s * count;
    segment.interval_s = interval_s;
    segment.record_size = 2 + 3 * 2;
    segment.record_count = count;
    for (std::uint32_t i = 0; i < count; ++i) {
        segment.records.push_back(init_s + (i + 0.5) * interval_s);
        segment.records.push_back(interval_s / 2);
        for (std::size_t k = 0; k < 3; ++k) {
            segment.records.push_back(base[k] + 0.125 * i);
            segment.records.push_back(slope[k]);
        }
    }
    segment.source = {"S-TEST", "synthetic.bpc segment 0"};
    return segment;
}

PckOrientation model_with(std::vector<PckSegment> segments) {
    PckOrientation model;
    model.body = BodyId::naif(1001);
    model.frame_name = "TEST_FIXED";
    model.pck_frame_name = "TEST_PCK";
    model.segments = std::move(segments);
    model.offset_source = {"S-TEST", "synthetic.tf TKFRAME_1"};
    return model;
}

} // namespace

TEST_CASE("binary PCK records give the 3-1-3 Euler rotation of their angles", "[frames][pck]") {
    const PckOrientation model =
        model_with({linear_segment(-1000.0, 100.0, 20, {0.5, 1.25, 3.0}, {0.01, -0.02, 0.4})});
    REQUIRE(frames::check_pck_orientation(model).has_value());
    // At t = 250 s: record 12, MID 250, s = 0, so the angles are the record's constant terms.
    const auto t = Instant::from_seconds_since_j2000(250.0);
    const auto transform = frames::pck_frame_transform(model, t);
    REQUIRE(transform.has_value());
    const double grow = 0.125 * 12;
    const frames::FrameTransform expected = frames::euler_313_transform(
        {0.5 + grow, 1.25 + grow, 3.0 + grow, 0.01 / 50.0, -0.02 / 50.0, 0.4 / 50.0});
    CHECK(m::max_abs_difference(transform->rotation(), expected.rotation()) == 0.0);
    CHECK(m::max_abs_difference(transform->rotation_rate_per_s(), expected.rotation_rate_per_s()) == 0.0);
    // R = [w]_3 [delta]_1 [phi]_3.
    const Mat3 r = frames::frame_rotation(3.0 + grow, 3) * frames::frame_rotation(1.25 + grow, 1) *
                   frames::frame_rotation(0.5 + grow, 3);
    CHECK(m::max_abs_difference(transform->rotation(), r) < 1e-15);
}

TEST_CASE("the rotation rate of a binary PCK frame equals the numerical derivative", "[frames][pck]") {
    const PckOrientation model =
        model_with({linear_segment(0.0, 1000.0, 4, {0.3, 0.7, 1.1}, {0.05, 0.02, 0.9})});
    const double t = 1234.5;
    const double h = 1e-3;
    const auto at = [&](double s) {
        return frames::pck_frame_transform(model, Instant::from_seconds_since_j2000(s));
    };
    const auto here = at(t);
    const auto before = at(t - h);
    const auto after = at(t + h);
    REQUIRE((here && before && after));
    const Mat3 numeric = (after->rotation() + before->rotation() * -1.0) * (1.0 / (2.0 * h));
    CHECK(m::max_abs_difference(here->rotation_rate_per_s(), numeric) < 1e-9);
}

TEST_CASE("later binary PCK segments take precedence and nothing is extrapolated", "[frames][pck]") {
    PckSegment early = linear_segment(0.0, 100.0, 10, {0.1, 0.2, 0.3}, {0.0, 0.0, 0.0});
    PckSegment late = linear_segment(500.0, 100.0, 10, {1.1, 1.2, 1.3}, {0.0, 0.0, 0.0});
    const PckOrientation model = model_with({early, late});
    const PckOrientation late_only = model_with({late});
    const PckOrientation early_only = model_with({early});
    const auto in_both = Instant::from_seconds_since_j2000(750.0);
    CHECK(frames::pck_frame_transform(model, in_both)->rotation() ==
          frames::pck_frame_transform(late_only, in_both)->rotation());
    const auto early_part = Instant::from_seconds_since_j2000(250.0);
    CHECK(frames::pck_frame_transform(model, early_part)->rotation() ==
          frames::pck_frame_transform(early_only, early_part)->rotation());
    CHECK(frames::pck_covers(model, Instant::from_seconds_since_j2000(1500.0)));
    CHECK_FALSE(frames::pck_covers(model, Instant::from_seconds_since_j2000(1500.5)));
    CHECK_FALSE(frames::pck_frame_transform(model, Instant::from_seconds_since_j2000(-0.5)).has_value());
    CHECK_FALSE(frames::pck_transform(model, Instant::from_seconds_since_j2000(2000.0)).has_value());
}

TEST_CASE("a segment's coverage may end a rounding error past its last record", "[frames][pck]") {
    // As after trimming a PCK with a record length that is not a whole number of seconds (DATA_PACK.md, ORNT).
    PckSegment segment = linear_segment(0.0, 100.0, 3, {0.1, 0.2, 0.3}, {0.0, 0.0, 0.5});
    segment.end_tdb_s = 300.0 + 5.0e-7;
    const PckOrientation model = model_with({segment});
    REQUIRE(frames::check_pck_orientation(model).has_value());
    const auto at_end = frames::pck_frame_transform(model, Instant::from_seconds_since_j2000(segment.end_tdb_s));
    const auto last = frames::pck_frame_transform(model, Instant::from_seconds_since_j2000(299.0));
    REQUIRE((at_end && last));
    // The last record is used: its w rate (0.5 / RADIUS) is the one in effect.
    CHECK(m::max_abs_difference(at_end->rotation(), last->rotation()) < 0.5 / 50.0 * 1.5);
    segment.end_tdb_s = 300.0 + 2.0e-6;
    CHECK_FALSE(frames::check_pck_orientation(model_with({segment})).has_value());
}

TEST_CASE("the frame kernel offset is the transpose of the TK angle matrix", "[frames][pck]") {
    PckOrientation model = model_with({linear_segment(0.0, 100.0, 1, {0.0, 0.0, 0.0}, {0.0, 0.0, 0.0})});
    SECTION("a single rotation about Z, in degrees") {
        model.offset_angles = {90.0, 0.0, 0.0};
        model.offset_axes = {3, 1, 2};
        model.offset_unit = AngleUnit::degrees;
        // [90 deg]_3 takes body-fixed vectors to the PCK frame; its transpose rotates vectors by +90 deg.
        CHECK(m::max_abs_difference(frames::pck_offset(model), Mat3::rotation_z(m::k_half_pi)) < 1e-15);
    }
    SECTION("three angles in arcseconds") {
        model.offset_angles = {3600.0, -7200.0, 1800.0};
        model.offset_axes = {3, 2, 1};
        model.offset_unit = AngleUnit::arcseconds;
        const double r = m::k_rad_per_arcsec;
        const Mat3 to_pck = frames::frame_rotation(3600.0 * r, 3) * frames::frame_rotation(-7200.0 * r, 2) *
                            frames::frame_rotation(1800.0 * r, 1);
        CHECK(m::max_abs_difference(frames::pck_offset(model), m::transpose(to_pck)) == 0.0);
        // The body-fixed transform applies the offset after the PCK frame.
        const auto t = Instant::from_seconds_since_j2000(50.0);
        const auto full = frames::pck_transform(model, t);
        REQUIRE(full.has_value());
        const Mat3 composed = frames::pck_offset(model) * frames::pck_frame_transform(model, t)->rotation();
        CHECK(m::max_abs_difference(full->rotation(), composed) == 0.0);
    }
}

TEST_CASE("a segment relative to ECLIPJ2000 is reached from the ICRF through the J2000 obliquity", "[frames][pck]") {
    PckSegment ecliptic = linear_segment(0.0, 100.0, 4, {0.3, 0.7, 1.1}, {0.05, 0.02, 0.9});
    ecliptic.base_frame = FrameKind::eclipj2000;
    PckSegment equatorial = ecliptic;
    equatorial.base_frame = FrameKind::icrf;
    const PckOrientation model = model_with({ecliptic});
    REQUIRE(frames::check_pck_orientation(model).has_value());
    const auto t = Instant::from_seconds_since_j2000(123.0);
    const auto via_ecliptic = frames::pck_frame_transform(model, t);
    const auto direct = frames::pck_frame_transform(model_with({equatorial}), t);
    REQUIRE((via_ecliptic && direct));
    // DATA_PACK.md, section ORNT: the rotation from the ICRF is R E and its rate dR/dt E.
    const Mat3 e = frames::icrf_to_eclipj2000();
    CHECK(m::max_abs_difference(via_ecliptic->rotation(), direct->rotation() * e) < 1e-15);
    CHECK(m::max_abs_difference(via_ecliptic->rotation_rate_per_s(), direct->rotation_rate_per_s() * e) < 1e-18);
    CHECK(m::max_abs_difference(via_ecliptic->rotation(), direct->rotation()) > 0.01); // E matters
}

TEST_CASE("a model without an offset is its binary PCK frame", "[frames][pck]") {
    PckOrientation model = model_with({linear_segment(0.0, 100.0, 5, {0.4, 0.5, 0.6}, {0.01, 0.0, 0.2})});
    model.offset_source.reset();
    model.frame_name = model.pck_frame_name;
    REQUIRE(frames::check_pck_orientation(model).has_value());
    CHECK(frames::pck_offset(model) == Mat3::identity());
    const auto t = Instant::from_seconds_since_j2000(321.0);
    const auto full = frames::pck_transform(model, t);
    const auto pck_frame = frames::pck_frame_transform(model, t);
    REQUIRE((full && pck_frame));
    CHECK(full->rotation() == pck_frame->rotation());
    CHECK(full->rotation_rate_per_s() == pck_frame->rotation_rate_per_s());

    const std::vector<PckOrientation> models{model};
    const FrameSystem system({}, models);
    const auto through_system = system.transform(FrameId::icrf(), FrameId::body_fixed(BodyId::naif(1001)), t);
    REQUIRE(through_system.has_value());
    CHECK(through_system.value().rotation() == pck_frame->rotation());

    SECTION("its frame name is the PCK frame's") {
        model.frame_name = "OTHER";
        CHECK_FALSE(frames::check_pck_orientation(model).has_value());
    }
    SECTION("its offset angles are zero") {
        model.offset_angles[1] = 1.0e-9;
        CHECK_FALSE(frames::check_pck_orientation(model).has_value());
    }
    SECTION("its axes are 1, 2, 3 and its unit radians") {
        model.offset_axes = {3, 2, 1};
        CHECK_FALSE(frames::check_pck_orientation(model).has_value());
        model.offset_axes = {1, 2, 3};
        model.offset_unit = AngleUnit::degrees;
        CHECK_FALSE(frames::check_pck_orientation(model).has_value());
    }
}

TEST_CASE("binary PCK models with broken records are rejected", "[frames][pck]") {
    const PckSegment good = linear_segment(0.0, 100.0, 3, {0.1, 0.2, 0.3}, {0.0, 0.0, 0.0});
    SECTION("no segments") {
        CHECK(frames::check_pck_orientation(model_with({})).error().code == ErrorCode::data_format);
    }
    SECTION("a record that does not span its interval") {
        PckSegment bad = good;
        bad.records[0] += 60.0; // MID of record 0
        CHECK(frames::check_pck_orientation(model_with({bad})).error().code == ErrorCode::data_format);
    }
    SECTION("a record size that is not 2 + 3 (degree + 1)") {
        PckSegment bad = good;
        bad.record_size = 7;
        CHECK_FALSE(frames::check_pck_orientation(model_with({bad})).has_value());
    }
    SECTION("coverage past the last record") {
        PckSegment bad = good;
        bad.end_tdb_s = 301.0;
        CHECK_FALSE(frames::check_pck_orientation(model_with({bad})).has_value());
    }
    SECTION("a base frame other than the ICRF or ECLIPJ2000") {
        PckSegment bad = good;
        bad.base_frame = FrameKind::body_fixed;
        CHECK_FALSE(frames::check_pck_orientation(model_with({bad})).has_value());
    }
    SECTION("an offset about an axis other than 1, 2 or 3") {
        PckOrientation model = model_with({good});
        model.offset_axes = {3, 0, 1};
        CHECK_FALSE(frames::check_pck_orientation(model).has_value());
    }
}

TEST_CASE("the frame system prefers a body's binary PCK model over its IAU elements", "[frames][pck]") {
    const std::vector<PckOrientation> models{
        model_with({linear_segment(0.0, 100.0, 5, {0.4, 0.5, 0.6}, {0.0, 0.0, 0.1})})};
    frames::IauRotationElements iau;
    iau.body = BodyId::naif(1001);
    iau.pole_ra_deg = {10.0};
    iau.pole_dec_deg = {80.0};
    iau.pm_deg = {20.0, 3.0};
    const FrameSystem system({iau}, models);
    CHECK(system.find_pck_orientation(BodyId::naif(1001)) == &models[0]);
    CHECK(system.has_body_fixed_frame(BodyId::naif(1001)));
    CHECK_FALSE(system.has_body_fixed_frame(BodyId::naif(1002)));

    const auto t = Instant::from_seconds_since_j2000(123.0);
    const auto transform = system.transform(FrameId::icrf(), FrameId::body_fixed(BodyId::naif(1001)), t);
    REQUIRE(transform.has_value());
    CHECK(transform.value().rotation() == frames::pck_transform(models[0], t)->rotation());

    // Outside the model's coverage there is no fallback to the IAU elements.
    const auto outside = system.transform(FrameId::icrf(), FrameId::body_fixed(BodyId::naif(1001)),
                                          Instant::from_seconds_since_j2000(600.0));
    REQUIRE_FALSE(outside.has_value());
    CHECK(outside.error().code == ErrorCode::out_of_coverage);

    // Without the model, the IAU elements define the frame.
    const FrameSystem iau_only({iau});
    const auto from_iau = iau_only.transform(FrameId::icrf(), FrameId::body_fixed(BodyId::naif(1001)), t);
    CHECK(from_iau.value().rotation() == frames::iau_transform(iau, t).rotation());
}
