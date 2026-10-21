// Round trip (P2-09): the synthetic pack written by the pipeline's writer, read by the core's reader, must
// hold exactly the contents the generator recorded in test_pack_expected.hpp.

#include "fixtures/test_pack_expected.hpp"
#include "usim/constants.hpp"
#include "usim/data/data_pack.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <bit>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <span>
#include <string>
#include <vector>

namespace expected = usim::test::pack;
using usim::ErrorCode;
using usim::catalog::BodyId;
using usim::data::DataPack;

namespace {

std::filesystem::path fixture_path() {
    return std::filesystem::path(USIM_TEST_FIXTURES_DIR) / expected::k_file_name;
}

std::vector<std::byte> fixture_bytes() {
    std::ifstream in(fixture_path(), std::ios::binary);
    std::vector<char> chars((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
    std::vector<std::byte> bytes(chars.size());
    std::memcpy(bytes.data(), chars.data(), chars.size());
    return bytes;
}

bool same_doubles(std::span<const double> a, std::span<const double> b) {
    // Bit-for-bit, so -0.0 and 0.0 differ and the test proves the bytes survived unchanged.
    return a.size() == b.size() && std::equal(a.begin(), a.end(), b.begin(), [](double x, double y) {
               return std::bit_cast<std::uint64_t>(x) == std::bit_cast<std::uint64_t>(y);
           });
}

const DataPack& pack() {
    static const DataPack loaded = [] {
        auto result = DataPack::load_file(fixture_path());
        if (!result) {
            FAIL("cannot load the fixture: " << result.error().message);
        }
        return std::move(result).value();
    }();
    return loaded;
}

void write_u16(std::vector<std::byte>& bytes, std::size_t at, std::uint16_t value) {
    bytes[at] = static_cast<std::byte>(value & 0xff);
    bytes[at + 1] = static_cast<std::byte>(value >> 8);
}

void rehash(std::vector<std::byte>& bytes) {
    const auto digest = usim::data::sha256(std::span(bytes).subspan(128));
    std::memcpy(bytes.data() + 32, digest.data(), digest.size());
}

ErrorCode parse_error_code(const std::vector<std::byte>& bytes) {
    const auto result = DataPack::parse(bytes);
    REQUIRE_FALSE(result.has_value());
    return result.error().code;
}

} // namespace

TEST_CASE("Data pack header and metadata round-trip", "[data][pack]") {
    const DataPack& p = pack();
    CHECK(p.header().version == usim::data::k_format_version);
    CHECK(p.header().file_size == expected::k_file_size);
    CHECK(usim::data::to_hex(p.header().content_sha256) == expected::k_content_sha256);
    CHECK(usim::data::to_hex(p.header().lock_sha256) == expected::k_lock_sha256);

    REQUIRE(p.metadata().size() == std::size(expected::k_metadata));
    for (std::size_t i = 0; i < p.metadata().size(); ++i) {
        CHECK(p.metadata()[i].first == expected::k_metadata[i].key);
        CHECK(p.metadata()[i].second == expected::k_metadata[i].value);
        CHECK(p.metadata_value(expected::k_metadata[i].key) == expected::k_metadata[i].value);
    }
    CHECK_FALSE(p.metadata_value("no.such.key").has_value());
}

TEST_CASE("Data pack sources and files round-trip", "[data][pack]") {
    const DataPack& p = pack();
    const auto sources = p.catalog().sources();
    REQUIRE(sources.size() == std::size(expected::k_sources));
    for (std::size_t i = 0; i < sources.size(); ++i) {
        const auto& e = expected::k_sources[i];
        CHECK(sources[i].id == e.id);
        CHECK(sources[i].title == e.title);
        CHECK(sources[i].provider == e.provider);
        CHECK(sources[i].url == e.url);
        CHECK(sources[i].citation == e.citation);
        CHECK(sources[i].license == e.license);
        CHECK(sources[i].tier == e.tier);
    }
    REQUIRE(p.source_files().size() == std::size(expected::k_files));
    for (std::size_t i = 0; i < p.source_files().size(); ++i) {
        const auto& f = p.source_files()[i];
        const auto& e = expected::k_files[i];
        CHECK(f.source_id == e.source_id);
        CHECK(f.name == e.name);
        CHECK(f.url == e.url);
        CHECK(f.retrieved_utc == e.retrieved_utc);
        CHECK(f.size_bytes == e.size);
        CHECK(usim::data::to_hex(f.sha256) == e.sha256);
        CHECK(usim::data::to_hex(f.md5) == e.md5);
        CHECK(f.publisher_checksum_verified == e.publisher_checksum_verified);
    }
}

TEST_CASE("Data pack bodies and quantities round-trip", "[data][pack]") {
    const auto& catalog = pack().catalog();
    REQUIRE(catalog.size() == std::size(expected::k_bodies));
    for (std::size_t i = 0; i < catalog.size(); ++i) {
        const auto& body = catalog.bodies()[i];
        const auto& e = expected::k_bodies[i];
        CHECK(body.id == BodyId::naif(e.naif_id));
        CHECK(static_cast<std::int32_t>(body.kind) == e.kind);
        CHECK(body.name == e.name);
        CHECK(body.primary.has_value() == e.has_primary);
        if (e.has_primary) {
            CHECK(*body.primary == BodyId::naif(e.primary_naif_id));
        }
    }

    std::size_t present = 0;
    for (const auto& body : catalog.bodies()) {
        for (const auto* q : {&body.gm, &body.mean_radius, &body.radius_a, &body.radius_b, &body.radius_c}) {
            present += q->has_value() ? 1 : 0;
        }
    }
    CHECK(present == std::size(expected::k_quantities));
    for (const auto& e : expected::k_quantities) {
        const auto* body = catalog.find(BodyId::naif(e.naif_id));
        REQUIRE(body != nullptr);
        const usim::catalog::Quantity* fields[] = {nullptr, &body->gm, &body->mean_radius, &body->radius_a,
                                                   &body->radius_b, &body->radius_c};
        const auto& q = *fields[e.field];
        INFO("body " << e.naif_id << " field " << e.field);
        REQUIRE(q.has_value());
        CHECK(std::bit_cast<std::uint64_t>(*q.value) == std::bit_cast<std::uint64_t>(e.value));
        CHECK(static_cast<std::int32_t>(q.unit) == e.unit);
        CHECK(q.uncertainty.has_value() == e.has_uncertainty);
        if (e.has_uncertainty) {
            CHECK(*q.uncertainty == e.uncertainty);
        }
        CHECK(q.source.source_id == e.source_id);
        CHECK(q.source.locator == e.locator);
        CHECK(q.derivation == e.derivation);
    }
    // A body without values keeps them missing, not zero.
    CHECK_FALSE(catalog.find(BodyId::naif(1003))->gm.has_value());
}

TEST_CASE("Data pack rotation elements round-trip", "[data][pack]") {
    const DataPack& p = pack();
    REQUIRE(p.rotation_elements().size() == std::size(expected::k_rotations));
    for (const auto& e : expected::k_rotations) {
        const auto* r = p.find_rotation(BodyId::naif(e.naif_id));
        REQUIRE(r != nullptr);
        CHECK(r->frame == usim::frames::FrameId::icrf());
        CHECK(r->source.source_id == e.source_id);
        CHECK(r->source.locator == e.locator);
        CHECK(r->phase_degree == e.phase_degree);
        CHECK(same_doubles(r->pole_ra_deg, e.pole_ra));
        CHECK(same_doubles(r->pole_dec_deg, e.pole_dec));
        CHECK(same_doubles(r->pm_deg, e.pm));
        CHECK(same_doubles(r->nut_prec_ra_deg, e.nut_prec_ra));
        CHECK(same_doubles(r->nut_prec_dec_deg, e.nut_prec_dec));
        CHECK(same_doubles(r->nut_prec_pm_deg, e.nut_prec_pm));
        CHECK(same_doubles(r->nut_prec_angles_deg, e.nut_prec_angles));
        if (e.nut_prec_angles.empty()) {
            CHECK_FALSE(r->angles_body.is_valid());
        } else {
            CHECK(r->angles_body == BodyId::naif(e.angles_naif_id));
            CHECK(r->angle_count() == e.nut_prec_angles.size() / static_cast<std::size_t>(e.phase_degree + 1));
        }
    }
    CHECK(p.find_rotation(BodyId::naif(1003)) == nullptr);
}

TEST_CASE("Data pack time data round-trip and drives TimeScales", "[data][pack]") {
    const DataPack& p = pack();
    CHECK(p.time_source().source_id == expected::k_time_source_id);
    CHECK(p.time_source().locator == expected::k_time_locator);
    CHECK(p.tdb_model().k_s == expected::k_tdb_k_s);
    CHECK(p.tdb_model().eb == expected::k_tdb_eb);
    CHECK(p.tdb_model().m0_rad == expected::k_tdb_m0_rad);
    CHECK(p.tdb_model().m1_rad_per_s == expected::k_tdb_m1_rad_per_s);
    const auto entries = p.leap_seconds().entries();
    REQUIRE(entries.size() == std::size(expected::k_leap_seconds));
    for (std::size_t i = 0; i < entries.size(); ++i) {
        CHECK(entries[i].effective_date.year == expected::k_leap_seconds[i].year);
        CHECK(entries[i].effective_date.month == expected::k_leap_seconds[i].month);
        CHECK(entries[i].effective_date.day == expected::k_leap_seconds[i].day);
        CHECK(entries[i].tai_minus_utc_s == expected::k_leap_seconds[i].tai_minus_utc_s);
    }
    // The synthetic table makes 1990-06-30 end with a leap second.
    const auto scales = p.time_scales();
    CHECK(scales.parse("1990-06-30T23:59:60.5 UTC").has_value());
    CHECK_FALSE(scales.parse("1991-06-30T23:59:60.5 UTC").has_value());
}

TEST_CASE("Data pack ephemeris segments round-trip bit for bit", "[data][pack]") {
    const DataPack& p = pack();
    REQUIRE(p.ephemeris_segments().size() == std::size(expected::k_segments));
    for (std::size_t i = 0; i < p.ephemeris_segments().size(); ++i) {
        const auto& s = p.ephemeris_segments()[i];
        const auto& e = expected::k_segments[i];
        INFO("segment " << i);
        CHECK(s.target == BodyId::naif(e.target_naif_id));
        CHECK(s.center == BodyId::naif(e.center_naif_id));
        CHECK(s.frame == usim::frames::FrameId::icrf());
        CHECK(s.spk_type == e.spk_type);
        CHECK(s.start_tdb_s == e.start_tdb_s);
        CHECK(s.end_tdb_s == e.end_tdb_s);
        CHECK(s.init_tdb_s == e.init_tdb_s);
        CHECK(s.interval_s == e.interval_s);
        CHECK(s.record_size == e.record_size);
        CHECK(s.record_count == e.record_count);
        CHECK(same_doubles(s.records, e.records));
        CHECK(s.source.source_id == e.source_id);
        CHECK(s.source.locator == e.locator);
    }
    CHECK(p.ephemeris_segments()[0].degree() == 2);
    CHECK(p.ephemeris_segments()[1].degree() == 1);
    CHECK(p.ephemeris_segments()[2].degree() == 0);
}

TEST_CASE("Parsing from memory gives the same pack as loading the file", "[data][pack]") {
    const auto bytes = fixture_bytes();
    REQUIRE(bytes.size() == expected::k_file_size);
    const auto parsed = DataPack::parse(bytes);
    REQUIRE(parsed.has_value());
    CHECK(parsed.value().header().content_sha256 == pack().header().content_sha256);
    CHECK(parsed.value().catalog().size() == pack().catalog().size());
}

TEST_CASE("The reader rejects damaged and incompatible packs", "[data][pack]") {
    const auto good = fixture_bytes();

    SECTION("a modified byte fails the content hash") {
        auto bytes = good;
        bytes[bytes.size() - 1] ^= std::byte{0x01};
        CHECK(parse_error_code(bytes) == ErrorCode::data_format);
    }
    SECTION("another major version is incompatible") {
        auto bytes = good;
        write_u16(bytes, 8, 2);
        CHECK(parse_error_code(bytes) == ErrorCode::incompatible_version);
    }
    SECTION("a newer minor version is accepted") {
        auto bytes = good;
        write_u16(bytes, 10, 7);
        const auto result = DataPack::parse(bytes);
        REQUIRE(result.has_value());
        CHECK(result.value().header().version.minor == 7);
    }
    SECTION("bad magic, truncation and Git LFS pointers") {
        auto bytes = good;
        bytes[0] = std::byte{'X'};
        CHECK(parse_error_code(bytes) == ErrorCode::data_format);
        CHECK(parse_error_code(std::vector<std::byte>(good.begin(), good.begin() + 100)) == ErrorCode::data_format);
        CHECK(parse_error_code(std::vector<std::byte>(good.begin(), good.end() - 8)) == ErrorCode::data_format);
        const std::string pointer = "version https://git-lfs.github.com/spec/v1\noid sha256:0\nsize 3592\n";
        std::vector<std::byte> lfs(pointer.size());
        std::memcpy(lfs.data(), pointer.data(), pointer.size());
        const auto result = DataPack::parse(lfs);
        REQUIRE_FALSE(result.has_value());
        CHECK(result.error().message.find("git lfs pull") != std::string::npos);
    }
    SECTION("a section pointing outside the file is rejected even with a valid hash") {
        auto bytes = good;
        bytes[128 + 8 + 3] = std::byte{0x7f}; // offset of the first section entry, high byte
        rehash(bytes);
        CHECK(parse_error_code(bytes) == ErrorCode::data_format);
    }
    SECTION("a changed TT - TAI is rejected") {
        // The TIME section holds TT - TAI at its byte 8; find it by value and replace it.
        auto bytes = good;
        const double value = usim::constants::k_tt_minus_tai_s;
        std::byte pattern[8];
        std::memcpy(pattern, &value, 8);
        const auto it = std::search(bytes.begin() + 128, bytes.end(), std::begin(pattern), std::end(pattern));
        REQUIRE(it != bytes.end());
        const double other = 32.0;
        std::memcpy(&*it, &other, 8);
        rehash(bytes);
        const auto result = DataPack::parse(bytes);
        REQUIRE_FALSE(result.has_value());
        CHECK(result.error().message.find("TT - TAI") != std::string::npos);
    }
    SECTION("a missing file is an I/O error") {
        const auto result = DataPack::load_file(std::filesystem::path(USIM_TEST_FIXTURES_DIR) / "no-such-pack.uspack");
        REQUIRE_FALSE(result.has_value());
        CHECK(result.error().code == ErrorCode::io_error);
    }
}
