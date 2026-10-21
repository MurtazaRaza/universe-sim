// Phase 2 acceptance: the default pack's catalog, rotation elements and time data equal the values written
// in the source text kernels exactly. The expected values come from data/reference/pack_kernel_values.csv,
// which holds each value's text as written in the kernel; this test parses that text itself (strtod), so the
// check does not depend on the pipeline's reading of the kernels.

#include "usim/constants.hpp"
#include "validation/validation_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <bit>
#include <cmath>
#include <map>
#include <regex>
#include <string>

namespace v = usim::test::validation;
using usim::catalog::BodyId;

namespace {

bool same(double a, double b) {
    return std::bit_cast<std::uint64_t>(a) == std::bit_cast<std::uint64_t>(b);
}

// "@1972-JAN-1" -> (1972, 1, 1)
usim::time::CalendarDate at_date(const std::string& text) {
    static const std::regex pattern(R"(@(\d{4})-([A-Z]{3})-(\d{1,2}))");
    static const std::string months = "JANFEBMARAPRMAYJUNJULAUGSEPOCTNOVDEC";
    std::smatch match;
    REQUIRE(std::regex_match(text, match, pattern));
    const auto month = months.find(match[2].str());
    REQUIRE(month != std::string::npos);
    return {std::stoi(match[1].str()), static_cast<std::int32_t>(month / 3 + 1), std::stoi(match[3].str())};
}

} // namespace

TEST_CASE("Default pack values equal the source kernels exactly", "[validation][pack]") {
    const auto& pack = v::default_pack();
    const auto table = v::read_reference("pack_kernel_values.csv");
    const auto file_col = table.column("file");
    const auto variable_col = table.column("variable");
    const auto index_col = table.column("index");
    const auto text_col = table.column("text");

    static const std::regex body_variable(R"(BODY(\d+)_([A-Z_]+))");
    std::map<std::string, std::size_t> matched; // "<what> <body>" -> number of values compared
    const auto& leaps = pack.leap_seconds().entries();
    std::size_t compared = 0;

    for (const auto& row : table.rows) {
        const std::string& file = row[file_col];
        const std::string& variable = row[variable_col];
        const auto index = static_cast<std::size_t>(std::stoul(row[index_col]));
        const std::string& text = row[text_col];
        INFO(file << " " << variable << "[" << index << "] = " << text);

        if (variable.starts_with("DELTET/")) {
            const auto& model = pack.tdb_model();
            if (variable == "DELTET/DELTA_AT") {
                REQUIRE(index / 2 < leaps.size());
                const auto& entry = leaps[index / 2];
                if (index % 2 == 0) {
                    CHECK(entry.tai_minus_utc_s == std::stoi(text));
                } else {
                    const auto date = at_date(text);
                    CHECK(entry.effective_date == date);
                }
            } else if (variable == "DELTET/DELTA_T_A") {
                CHECK(same(v::parse_double(text), usim::constants::k_tt_minus_tai_s));
            } else if (variable == "DELTET/K") {
                CHECK(same(v::parse_double(text), model.k_s));
            } else if (variable == "DELTET/EB") {
                CHECK(same(v::parse_double(text), model.eb));
            } else if (variable == "DELTET/M") {
                CHECK(same(v::parse_double(text), index == 0 ? model.m0_rad : model.m1_rad_per_s));
            } else {
                continue;
            }
            ++matched[variable];
            ++compared;
            continue;
        }

        std::smatch match;
        REQUIRE(std::regex_match(variable, match, body_variable));
        const BodyId id = BodyId::naif(std::stoll(match[1].str()));
        const std::string item = match[2].str();
        const std::string key = item + " " + match[1].str();
        const auto* body = pack.catalog().find(id);
        const double value = v::parse_double(text);

        if (item == "GM" && body != nullptr) {
            REQUIRE(body->gm.has_value());
            CHECK(same(*body->gm.value, value));
            CHECK(body->gm.source.locator == file + " " + variable);
        } else if (item == "RADII" && body != nullptr) {
            const usim::catalog::Quantity* radii[] = {&body->radius_a, &body->radius_b, &body->radius_c};
            REQUIRE(index < 3);
            REQUIRE(radii[index]->has_value());
            CHECK(same(*radii[index]->value, value));
            CHECK(radii[index]->source.locator == file + " " + variable + "[" + std::to_string(index) + "]");
        } else if (item == "POLE_RA" || item == "POLE_DEC" || item == "PM" || item == "NUT_PREC_RA" ||
                   item == "NUT_PREC_DEC" || item == "NUT_PREC_PM") {
            const auto* r = pack.find_rotation(id);
            if (r == nullptr) {
                continue;
            }
            const std::map<std::string, const std::vector<double>*> arrays = {
                {"POLE_RA", &r->pole_ra_deg},         {"POLE_DEC", &r->pole_dec_deg},
                {"PM", &r->pm_deg},                   {"NUT_PREC_RA", &r->nut_prec_ra_deg},
                {"NUT_PREC_DEC", &r->nut_prec_dec_deg}, {"NUT_PREC_PM", &r->nut_prec_pm_deg}};
            const auto& values = *arrays.at(item);
            REQUIRE(index < values.size());
            CHECK(same(values[index], value));
        } else if (item == "NUT_PREC_ANGLES" || item == "MAX_PHASE_DEGREE") {
            bool used = false;
            for (const auto& r : pack.rotation_elements()) {
                if (r.angles_body != id) {
                    continue;
                }
                used = true;
                if (item == "MAX_PHASE_DEGREE") {
                    CHECK(r.phase_degree == std::stoi(text));
                } else {
                    REQUIRE(index < r.nut_prec_angles_deg.size());
                    CHECK(same(r.nut_prec_angles_deg[index], value));
                }
            }
            if (!used) {
                continue;
            }
        } else {
            continue; // a kernel variable the pack does not carry (for example BODY399_LONG_AXIS)
        }
        ++matched[key];
        ++compared;
    }

    // Every kernel value in the pack was compared: nothing in the pack lacks a reference row.
    for (const auto& body : pack.catalog().bodies()) {
        const std::string id = std::to_string(body.id.value);
        CHECK(matched["GM " + id] == (body.gm.has_value() ? 1u : 0u));
        CHECK(matched["RADII " + id] == (body.radius_a.has_value() ? 3u : 0u));
        if (body.mean_radius.has_value()) {
            // Derived: cbrt(a b c), computed by the pipeline with correct rounding; std::cbrt is within an ulp.
            const double expected = std::cbrt(*body.radius_a.value * *body.radius_b.value * *body.radius_c.value);
            const double ulp = std::nextafter(expected, INFINITY) - expected;
            CHECK(std::abs(*body.mean_radius.value - expected) <= 2 * ulp);
            CHECK(body.mean_radius.is_derived());
        }
    }
    for (const auto& r : pack.rotation_elements()) {
        const std::string id = std::to_string(r.body.value);
        CHECK(matched["POLE_RA " + id] == r.pole_ra_deg.size());
        CHECK(matched["POLE_DEC " + id] == r.pole_dec_deg.size());
        CHECK(matched["PM " + id] == r.pm_deg.size());
        CHECK(matched["NUT_PREC_RA " + id] == r.nut_prec_ra_deg.size());
        CHECK(matched["NUT_PREC_DEC " + id] == r.nut_prec_dec_deg.size());
        CHECK(matched["NUT_PREC_PM " + id] == r.nut_prec_pm_deg.size());
        if (!r.nut_prec_angles_deg.empty()) {
            CHECK(matched["NUT_PREC_ANGLES " + std::to_string(r.angles_body.value)] > 0);
        }
    }
    CHECK(matched["DELTET/DELTA_AT"] == 2 * leaps.size());
    CHECK(compared > 0);
}

TEST_CASE("Default pack provenance is complete", "[validation][pack]") {
    const auto& pack = v::default_pack();
    CHECK(pack.metadata_value("pack.name") == "solar-system");
    // Every file the pack was built from is cited, and every value cites a registered source.
    for (const auto& file : pack.source_files()) {
        CHECK(pack.catalog().find_source(file.source_id) != nullptr);
    }
    for (const auto& body : pack.catalog().bodies()) {
        for (const auto* q : {&body.gm, &body.mean_radius, &body.radius_a, &body.radius_b, &body.radius_c}) {
            if (q->has_value()) {
                CHECK(pack.catalog().find_source(q->source.source_id) != nullptr);
                CHECK_FALSE(q->source.locator.empty());
            }
        }
    }
    for (const auto& s : pack.ephemeris_segments()) {
        CHECK(pack.catalog().find_source(s.source.source_id) != nullptr);
        CHECK(s.source.locator.starts_with("de440s.bsp segment "));
    }
}
