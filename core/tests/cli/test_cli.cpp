// P4-07: the usim command line on the synthetic round-trip pack (made-up values, not physical data).

#include "cli.hpp"
#include "fixtures/test_pack_expected.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <filesystem>
#include <sstream>
#include <string>
#include <vector>

namespace expected = usim::test::pack;

namespace {

struct Output {
    int code;
    std::string out;
    std::string err;
};

Output run(std::vector<std::string> args, bool with_pack = true) {
    if (with_pack) {
        args.push_back("--pack");
        args.push_back((std::filesystem::path(USIM_TEST_FIXTURES_DIR) / expected::k_file_name).string());
    }
    std::ostringstream out;
    std::ostringstream err;
    const int code = usim::cli::run(args, out, err);
    return {code, out.str(), err.str()};
}

bool contains(const std::string& text, const std::string& part) {
    return text.find(part) != std::string::npos;
}

std::size_t lines(const std::string& text) {
    return static_cast<std::size_t>(std::count(text.begin(), text.end(), '\n'));
}

} // namespace

TEST_CASE("usim prints help and version without a pack", "[cli]") {
    const Output help = run({}, false);
    CHECK(help.code == usim::cli::k_exit_ok);
    CHECK(contains(help.out, "Commands:"));
    const Output version = run({"--version"}, false);
    CHECK(version.code == 0);
    CHECK(contains(version.out, "usim 0."));
}

TEST_CASE("usim rejects bad command lines", "[cli]") {
    CHECK(run({"frobnicate"}).code == usim::cli::k_exit_usage);
    CHECK(run({"state", "1001", "--colour", "red"}).code == usim::cli::k_exit_usage);
    CHECK(run({"state", "1001", "--format"}).code == usim::cli::k_exit_usage);
    CHECK(run({"state", "1001", "--format", "xml"}).code == usim::cli::k_exit_usage);
    CHECK(run({"state"}).code == usim::cli::k_exit_usage);
    CHECK(run({"state", "1001", "--frame", "galactic"}).code == usim::cli::k_exit_usage);
    CHECK(run({"snapshot", "--axes", "sideways"}).code == usim::cli::k_exit_usage);
    CHECK(run({"snapshot", "--km-per-unit", "lots"}).code == usim::cli::k_exit_usage);
    const Output missing = run({"coverage", "--pack", "/no/such.uspack"}, false);
    CHECK(missing.code == usim::cli::k_exit_failed);
    CHECK(contains(missing.err, "io_error"));
}

TEST_CASE("usim state accepts IDs and names, in JSON and CSV", "[cli]") {
    const Output json = run({"state", "1002", "--center", "test star", "--time", "2000-01-01T23:00:00 TDB"});
    REQUIRE(json.code == 0);
    CHECK(contains(json.out, R"("body":{"id":"naif:1002","name":"Test Moon"})"));
    CHECK(contains(json.out, R"("center":{"id":"naif:1000","name":"Test Star"})"));
    CHECK(contains(json.out, R"("frame":"ECLIPJ2000")"));
    CHECK(contains(json.out, R"("approximate":false)"));
    const Output csv = run(
        {"state", "Test Körper", "--frame", "icrf", "--time", "2000-01-01T23:00:00 TDB", "--format", "csv"});
    REQUIRE(csv.code == 0);
    CHECK(lines(csv.out) == 2);
    CHECK(contains(csv.out, "naif:1003,naif:0,ICRF,"));
    CHECK(contains(csv.out, ",ephemeris,yes\n"));
    const Output body_fixed = run(
        {"state", "1002", "--center", "1001", "--frame", "body:1001", "--time", "2000-01-01T23:00:00 TDB"});
    CHECK(body_fixed.code == 0);
    CHECK(contains(body_fixed.out, "body-fixed(naif:1001)"));
}

TEST_CASE("usim reports query failures with the status", "[cli]") {
    const Output outside = run({"state", "1002"}); // J2000 is before 1001's coverage
    CHECK(outside.code == usim::cli::k_exit_failed);
    CHECK(contains(outside.err, "(out_of_coverage)"));
    const Output unknown = run({"state", "nobody"});
    CHECK(unknown.code == usim::cli::k_exit_failed);
    CHECK(contains(unknown.err, "(not_found)"));
}

TEST_CASE("usim catalog and coverage", "[cli]") {
    const Output json = run({"catalog"});
    REQUIRE(json.code == 0);
    CHECK(contains(json.out, R"("format":"1.2")"));
    CHECK(contains(
        json.out,
        R"("position":"approximate","approximation":"Synthetic: no segment; placed at its primary")"));
    CHECK(contains(json.out, R"("source":"S-TEST-A")"));
    CHECK(contains(json.out, "\"name\":\"Test K\xc3\xb6rper\""));
    const Output csv = run({"catalog", "--format", "csv"});
    REQUIRE(csv.code == 0);
    CHECK(lines(csv.out) == 1 + std::size(expected::k_bodies));
    const Output coverage = run({"coverage", "--format", "csv"});
    REQUIRE(coverage.code == 0);
    CHECK(contains(coverage.out, ",3600,86400\n"));
}

TEST_CASE("usim snapshot, orientation and orbit", "[cli]") {
    const Output snapshot =
        run({"snapshot", "--center", "1000", "--time", "2000-01-01T20:00:00 TDB", "--axes", "unity",
             "--km-per-unit", "0.5", "--enlarge", "2", "--format", "csv"});
    REQUIRE(snapshot.code == 0);
    CHECK(lines(snapshot.out) == 1 + std::size(expected::k_bodies));
    CHECK(contains(snapshot.out, "naif:1003,"));
    CHECK(contains(snapshot.out, "approximate"));
    const Output snapshot_json = run({"snapshot", "--time", "2000-01-01T20:00:00 TDB"});
    REQUIRE(snapshot_json.code == 0);
    CHECK(contains(snapshot_json.out, R"("scale_mode":"true-scale")"));

    const Output orientation = run({"orientation", "1001", "--time", "2000-01-01T20:00:00 TDB"});
    REQUIRE(orientation.code == 0);
    CHECK(contains(orientation.out, "\"quaternion_wxyz\":["));
    CHECK(run({"orientation", "1002"}).code == usim::cli::k_exit_failed); // no rotation model

    const Output orbit = run({"orbit", "1001", "--time", "2000-01-01T20:00:00 TDB", "--format", "csv"});
    REQUIRE(orbit.code == 0);
    CHECK(lines(orbit.out) > 3);
    CHECK(contains(orbit.out, "tdb_seconds,x_km,y_km,z_km\n"));
    const Output orbit_json = run({"orbit", "1001", "--center", "0", "--time", "2000-01-01T20:00:00 TDB"});
    REQUIRE(orbit_json.code == 0);
    CHECK(contains(orbit_json.out, R"("center":{"id":"naif:0")"));
}
