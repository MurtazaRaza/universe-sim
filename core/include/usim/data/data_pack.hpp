#pragma once

#include "usim/catalog/body_catalog.hpp"
#include "usim/catalog/body_id.hpp"
#include "usim/catalog/quantity.hpp"
#include "usim/catalog/sky.hpp"
#include "usim/data/sha256.hpp"
#include "usim/frames/frame_id.hpp"
#include "usim/frames/iau_rotation.hpp"
#include "usim/result.hpp"
#include "usim/time/time_scales.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace usim::data {

/// Version of the data pack format (docs/formats/DATA_PACK.md). A reader accepts every minor version of
/// its major version: minor versions only add sections, which older readers skip.
struct FormatVersion {
    std::uint16_t major = 0; ///< Incompatible changes.
    std::uint16_t minor = 0; ///< Additions older readers can ignore.

    friend constexpr bool operator==(const FormatVersion&, const FormatVersion&) noexcept = default;
};

/// The format version this reader implements.
inline constexpr FormatVersion k_format_version{1, 2};

/// The fixed header of a pack.
struct DataPackHeader {
    FormatVersion version;        ///< Format version of the file.
    std::uint64_t file_size = 0;  ///< Size of the whole file in bytes.
    Sha256Digest content_sha256{}; ///< SHA-256 of every byte after the header; verified when the pack loads.
    Sha256Digest lock_sha256{};    ///< SHA-256 of the canonical data/sources.lock.json the pack was built from.
};

/// A source file the pack was built from, as pinned in data/sources.lock.json.
struct SourceFile {
    std::string source_id;     ///< The registry entry providing the file (catalog::Source::id).
    std::string name;          ///< File name, for example "de440s.bsp".
    std::string url;           ///< Where it was downloaded from.
    std::string retrieved_utc; ///< When it was downloaded, ISO 8601 UTC ("2026-10-20T15:09:49Z").
    std::uint64_t size_bytes = 0;            ///< File size.
    Sha256Digest sha256{};                   ///< SHA-256 of the file.
    std::array<std::uint8_t, 16> md5{};      ///< MD5 of the file.
    bool publisher_checksum_verified = false; ///< True if the MD5 matched a checksum list published by the provider.
};

/// The Chebyshev records of one SPK type 2 or type 3 segment (DATA_PACK.md, sections EPHS and EPHD).
///
/// Record i covers [init_tdb_s + i interval_s, init_tdb_s + (i + 1) interval_s] and holds record_size
/// doubles: MID and RADIUS (TDB seconds; the record's time argument is (t - MID) / RADIUS), then the
/// Chebyshev coefficients of X, Y, Z in km (type 2), followed by those of VX, VY, VZ in km/s (type 3), each
/// degree() + 1 long. When several segments of one target cover an instant, the later segment in the pack
/// takes precedence, as in SPICE.
struct EphemerisSegment {
    catalog::BodyId target;            ///< The body whose position the segment gives.
    catalog::BodyId center;            ///< The body it is relative to.
    frames::FrameId frame = frames::FrameId::icrf(); ///< Frame of the positions and velocities.
    std::int32_t spk_type = 0;         ///< 2 (position) or 3 (position and velocity).
    double start_tdb_s = 0.0;          ///< Start of coverage, TDB seconds past J2000 (inclusive).
    double end_tdb_s = 0.0;            ///< End of coverage, TDB seconds past J2000 (inclusive).
    double init_tdb_s = 0.0;           ///< Start of the first record, TDB seconds past J2000.
    double interval_s = 0.0;           ///< Length of every record, seconds.
    std::uint32_t record_size = 0;     ///< Doubles per record.
    std::uint32_t record_count = 0;    ///< Number of records.
    std::vector<double> records;       ///< record_count * record_size doubles, record after record.
    catalog::SourceRef source;         ///< The SPK file and segment the records were copied from.

    /// Degree of the Chebyshev polynomials.
    [[nodiscard]] std::uint32_t degree() const noexcept {
        return (record_size - 2) / (spk_type == 3 ? 6u : 3u) - 1;
    }
};

/// A body without an ephemeris segment of its own, placed at its primary instead, with every state flagged
/// approximate (DATA_PACK.md, section APPX; ADR-0008). For example de440s.bsp has no segment for the center
/// of Jupiter, so Jupiter is placed at the Jupiter system barycenter.
struct PositionApproximation {
    catalog::BodyId body;  ///< The approximated body.
    catalog::BodyId proxy; ///< Where it is placed: its primary in the catalog.
    std::string reason;    ///< Why, for display and provenance.
};

/// A loaded, validated data pack (SPEC FR-DATA-06): the catalog with per-value provenance, time scale data,
/// IAU rotation elements and ephemeris segments. Immutable; safe to share between threads.
class DataPack {
public:
    /// Parses and validates a pack held in memory. Nothing is kept pointing into `bytes`. Fails with
    /// ErrorCode::incompatible_version for another major format version, and ErrorCode::data_format for
    /// anything malformed or inconsistent, including a content hash that does not match.
    [[nodiscard]] static Result<DataPack> parse(std::span<const std::byte> bytes);

    /// Reads the file at `path` and parses it. Fails with ErrorCode::io_error if the file cannot be read,
    /// otherwise like parse().
    [[nodiscard]] static Result<DataPack> load_file(const std::filesystem::path& path);

    /// The header.
    [[nodiscard]] const DataPackHeader& header() const noexcept { return header_; }

    /// Metadata entries (for example "pack.name"), sorted by key.
    [[nodiscard]] std::span<const std::pair<std::string, std::string>> metadata() const noexcept { return metadata_; }

    /// The metadata value for `key`, if present.
    [[nodiscard]] std::optional<std::string_view> metadata_value(std::string_view key) const noexcept;

    /// The bodies, their hierarchy, their sourced values and the source table.
    [[nodiscard]] const catalog::BodyCatalog& catalog() const noexcept { return catalog_; }

    /// The files the pack was built from.
    [[nodiscard]] std::span<const SourceFile> source_files() const noexcept { return files_; }

    /// The leap second table.
    [[nodiscard]] const time::LeapSecondTable& leap_seconds() const noexcept { return leap_seconds_; }

    /// Parameters of the TDB - TT model.
    [[nodiscard]] const time::TdbModel& tdb_model() const noexcept { return tdb_model_; }

    /// Where the leap second table and the TDB - TT model came from.
    [[nodiscard]] const catalog::SourceRef& time_source() const noexcept { return time_source_; }

    /// A time scale converter using the pack's leap seconds and TDB model.
    [[nodiscard]] time::TimeScales time_scales() const { return time::TimeScales(leap_seconds_, tdb_model_); }

    /// IAU rotation elements, in pack order.
    [[nodiscard]] std::span<const frames::IauRotationElements> rotation_elements() const noexcept { return rotations_; }

    /// The rotation elements of `body`, or nullptr.
    [[nodiscard]] const frames::IauRotationElements* find_rotation(catalog::BodyId body) const noexcept;

    /// Ephemeris segments, in precedence order (later segments win where they overlap).
    [[nodiscard]] std::span<const EphemerisSegment> ephemeris_segments() const noexcept { return segments_; }

    /// Bodies placed at their primary, in catalog order. Empty for a format 1.0 pack.
    [[nodiscard]] std::span<const PositionApproximation> position_approximations() const noexcept {
        return approximations_;
    }

    /// The star catalog of the sky background; no stars for a pack without one (format 1.2, section STAR).
    [[nodiscard]] const catalog::StarCatalog& star_catalog() const noexcept { return stars_; }

    /// Ring regions and gaps, sorted by body (in catalog order) and inner radius; empty for a pack without
    /// rings (format 1.2, section RING).
    [[nodiscard]] std::span<const catalog::Ring> rings() const noexcept { return rings_; }

private:
    DataPack(DataPackHeader header, std::vector<std::pair<std::string, std::string>> metadata,
             catalog::BodyCatalog catalog, std::vector<SourceFile> files, time::LeapSecondTable leap_seconds,
             time::TdbModel tdb_model, catalog::SourceRef time_source,
             std::vector<frames::IauRotationElements> rotations, std::vector<EphemerisSegment> segments,
             std::vector<PositionApproximation> approximations, catalog::StarCatalog stars,
             std::vector<catalog::Ring> rings);

    DataPackHeader header_;
    std::vector<std::pair<std::string, std::string>> metadata_;
    catalog::BodyCatalog catalog_;
    std::vector<SourceFile> files_;
    time::LeapSecondTable leap_seconds_;
    time::TdbModel tdb_model_;
    catalog::SourceRef time_source_;
    std::vector<frames::IauRotationElements> rotations_;
    std::vector<EphemerisSegment> segments_;
    std::vector<PositionApproximation> approximations_;
    catalog::StarCatalog stars_;
    std::vector<catalog::Ring> rings_;
};

} // namespace usim::data
