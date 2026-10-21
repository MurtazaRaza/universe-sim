#include "usim/catalog/body.hpp"
#include "usim/catalog/body_catalog.hpp"
#include "usim/catalog/body_id.hpp"
#include "usim/catalog/quantity.hpp"

#include <catch2/catch_test_macros.hpp>

#include <limits>
#include <optional>
#include <string>
#include <vector>

using usim::ErrorCode;
using usim::catalog::Body;
using usim::catalog::BodyCatalog;
using usim::catalog::BodyId;
using usim::catalog::BodyKind;
using usim::catalog::Quantity;
using usim::catalog::Source;
using usim::catalog::Unit;

// The values in these tests are synthetic (round numbers from a fake source). They test the catalog's
// structure, not physics; real values only ever come from data packs.

namespace {

const BodyId k_ssb = BodyId::naif(0);
const BodyId k_sun = BodyId::naif(10);
const BodyId k_emb = BodyId::naif(3);
const BodyId k_earth = BodyId::naif(399);
const BodyId k_moon = BodyId::naif(301);

std::vector<Source> test_sources() {
    return {Source{"TEST-A", "Synthetic source A", "Unit tests", "", "", "", 1},
            Source{"TEST-B", "Synthetic source B", "Unit tests", "", "", "", 2}};
}

Quantity sourced(double value, Unit unit, std::string source = "TEST-A") {
    Quantity q = Quantity::missing(unit);
    q.value = value;
    q.source = {std::move(source), "synthetic"};
    return q;
}

Body body(BodyId id, std::string name, BodyKind kind, std::optional<BodyId> primary) {
    Body b;
    b.id = id;
    b.name = std::move(name);
    b.kind = kind;
    b.primary = primary;
    return b;
}

std::vector<Body> test_bodies() {
    std::vector<Body> bodies{
        body(k_ssb, "Solar System Barycenter", BodyKind::barycenter, std::nullopt),
        body(k_sun, "Sun", BodyKind::star, k_ssb),
        body(k_emb, "Earth-Moon Barycenter", BodyKind::barycenter, k_ssb),
        body(k_earth, "Earth", BodyKind::planet, k_emb),
        body(k_moon, "Moon", BodyKind::moon, k_emb),
    };
    bodies[3].gm = sourced(100.0, Unit::km3_per_s2);
    bodies[3].mean_radius = sourced(10.0, Unit::km, "TEST-B");
    bodies[3].mean_radius.derivation = "(a + b + c) / 3 from radius_a, radius_b, radius_c";
    bodies[3].mean_radius.uncertainty = 0.5;
    return bodies;
}

ErrorCode create_error(std::vector<Source> sources, std::vector<Body> bodies) {
    const auto r = BodyCatalog::create(std::move(sources), std::move(bodies));
    REQUIRE_FALSE(r.has_value());
    return r.error().code;
}

} // namespace

TEST_CASE("catalog lookup and hierarchy", "[catalog]") {
    const auto created = BodyCatalog::create(test_sources(), test_bodies());
    REQUIRE(created.has_value());
    const BodyCatalog& catalog = created.value();

    CHECK(catalog.size() == 5);
    REQUIRE(catalog.find(k_earth) != nullptr);
    CHECK(catalog.find(k_earth)->name == "Earth");
    CHECK(catalog.find(BodyId::naif(499)) == nullptr);
    CHECK(catalog.find_source("TEST-B")->tier == 2);
    CHECK(catalog.find_source("NOPE") == nullptr);

    // Bodies keep their given order.
    CHECK(catalog.bodies()[1].id == k_sun);
    CHECK(catalog.roots() == std::vector<BodyId>{k_ssb});
    CHECK(catalog.children(k_ssb) == std::vector<BodyId>{k_sun, k_emb});
    CHECK(catalog.children(k_emb) == std::vector<BodyId>{k_earth, k_moon});
    CHECK(catalog.children(k_moon).empty());
    CHECK(catalog.ancestry(k_moon).value() == std::vector<BodyId>{k_moon, k_emb, k_ssb});
    CHECK(catalog.ancestry(BodyId::naif(499)).error().code == ErrorCode::not_found);
}

TEST_CASE("quantities carry provenance and may be missing", "[catalog]") {
    const auto catalog = BodyCatalog::create(test_sources(), test_bodies()).value();
    const Body& earth = *catalog.find(k_earth);
    CHECK(earth.gm.has_value());
    CHECK(earth.gm.unit == Unit::km3_per_s2);
    CHECK(earth.gm.source.source_id == "TEST-A");
    CHECK_FALSE(earth.gm.is_derived());
    CHECK(earth.mean_radius.is_derived());
    CHECK(earth.mean_radius.uncertainty == 0.5);

    // Missing stays missing: no value, not zero.
    CHECK_FALSE(earth.radius_a.has_value());
    CHECK_FALSE(catalog.find(k_moon)->gm.has_value());
    CHECK(to_string(earth.gm.unit) == "km^3/s^2");
}

TEST_CASE("catalog validation", "[catalog]") {
    SECTION("duplicate body ids") {
        auto bodies = test_bodies();
        bodies.push_back(body(k_earth, "Earth again", BodyKind::planet, k_emb));
        CHECK(create_error(test_sources(), bodies) == ErrorCode::data_format);
    }
    SECTION("unknown primary") {
        auto bodies = test_bodies();
        bodies.push_back(body(BodyId::naif(401), "Phobos", BodyKind::moon, BodyId::naif(4)));
        CHECK(create_error(test_sources(), bodies) == ErrorCode::data_format);
    }
    SECTION("primary cycle") {
        std::vector<Body> bodies{body(BodyId::naif(1), "A", BodyKind::barycenter, BodyId::naif(2)),
                                 body(BodyId::naif(2), "B", BodyKind::barycenter, BodyId::naif(1))};
        CHECK(create_error(test_sources(), bodies) == ErrorCode::data_format);
    }
    SECTION("self as primary") {
        std::vector<Body> bodies{body(BodyId::naif(1), "A", BodyKind::barycenter, BodyId::naif(1))};
        CHECK(create_error(test_sources(), bodies) == ErrorCode::data_format);
    }
    SECTION("invalid id and empty name") {
        CHECK(create_error(test_sources(), {body(BodyId{}, "X", BodyKind::planet, std::nullopt)}) ==
              ErrorCode::data_format);
        CHECK(create_error(test_sources(), {body(k_ssb, "", BodyKind::planet, std::nullopt)}) ==
              ErrorCode::data_format);
    }
    SECTION("unregistered source") {
        auto bodies = test_bodies();
        bodies[1].gm = sourced(1.0, Unit::km3_per_s2, "UNKNOWN");
        CHECK(create_error(test_sources(), bodies) == ErrorCode::data_format);
    }
    SECTION("value without a source") {
        auto bodies = test_bodies();
        bodies[1].gm = sourced(1.0, Unit::km3_per_s2, "");
        CHECK(create_error(test_sources(), bodies) == ErrorCode::data_format);
    }
    SECTION("non-finite value or bad uncertainty") {
        auto bodies = test_bodies();
        bodies[1].gm = sourced(std::numeric_limits<double>::quiet_NaN(), Unit::km3_per_s2);
        CHECK(create_error(test_sources(), bodies) == ErrorCode::data_format);

        bodies = test_bodies();
        bodies[1].gm = sourced(1.0, Unit::km3_per_s2);
        bodies[1].gm.uncertainty = -1.0;
        CHECK(create_error(test_sources(), bodies) == ErrorCode::data_format);

        bodies = test_bodies();
        bodies[1].gm.uncertainty = 1.0; // with no value
        CHECK(create_error(test_sources(), bodies) == ErrorCode::data_format);
    }
    SECTION("duplicate or empty source ids") {
        auto sources = test_sources();
        sources.push_back(sources[0]);
        CHECK(create_error(sources, test_bodies()) == ErrorCode::data_format);
        sources = test_sources();
        sources[1].id.clear();
        CHECK(create_error(sources, test_bodies()) == ErrorCode::data_format);
    }
}

TEST_CASE("identifier and kind names", "[catalog]") {
    CHECK(to_string(k_earth) == "naif:399");
    CHECK(to_string(BodyId{usim::catalog::IdNamespace::gaia_dr3, 42}) == "gaia-dr3:42");
    CHECK(to_string(BodyKind::dwarf_planet) == "dwarf_planet");
    CHECK(k_sun < k_earth);
    CHECK_FALSE(BodyId{}.is_valid());
    CHECK(BodyCatalog().size() == 0);
}
