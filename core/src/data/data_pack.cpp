// Reader of data pack format v1. The layout is specified in docs/formats/DATA_PACK.md; the pipeline's
// writer (tools/pipeline/usim_pipeline/pack/binary.py) implements the same specification independently.

#include "usim/data/data_pack.hpp"

#include "usim/constants.hpp"

#include <algorithm>
#include <bit>
#include <cmath>
#include <cstring>
#include <fstream>
#include <ios>
#include <limits>
#include <string>
#include <utility>

namespace usim::data {

namespace {

constexpr std::string_view k_magic = "USIMPACK";
constexpr std::string_view k_lfs_pointer = "version https://git-lfs";
constexpr std::uint32_t k_endian_marker = 0x01020304;
constexpr std::size_t k_header_size = 128;
constexpr std::size_t k_section_entry_size = 32;
constexpr std::uint32_t k_section_version = 1;
constexpr std::uint64_t k_alignment = 8;

// Record sizes in bytes (DATA_PACK.md).
constexpr std::size_t k_counts_size = 8;
constexpr std::size_t k_meta_size = 16;
constexpr std::size_t k_source_size = 56;
constexpr std::size_t k_file_size = 88;
constexpr std::size_t k_body_size = 40;
constexpr std::size_t k_quantity_size = 56;
constexpr std::size_t k_rotation_size = 88;
constexpr std::size_t k_time_size = 56;
constexpr std::size_t k_leap_size = 16;
constexpr std::size_t k_segment_size = 96;
constexpr std::size_t k_approximation_size = 32;
constexpr std::size_t k_star_header_size = 32;
constexpr std::size_t k_star_size = 72;
constexpr std::size_t k_ring_size = 72;

constexpr std::uint32_t k_max_phase_degree = 16; // DATA_PACK.md, ROTN
constexpr std::uint32_t k_quantity_has_uncertainty = 1;
constexpr std::uint32_t k_file_publisher_verified = 1;
constexpr std::uint32_t k_star_has_color = 1;
constexpr std::uint32_t k_star_has_proper_motion = 2;
constexpr std::uint32_t k_ring_gap = 1;
constexpr std::uint32_t k_ring_has_optical_depth = 2;
constexpr double k_pi = 3.141592653589793; // the double nearest pi

// The sections of format 1.0, all required.
constexpr std::array<std::string_view, 10> k_required_sections = {"META", "SRCS", "FILE", "BODY", "QNTY",
                                                                  "ROTN", "TIME", "EPHS", "STRS", "EPHD"};

// Sections added by later minor versions, optional (1.1: APPX; 1.2: STAR, RING).
constexpr std::array<std::string_view, 3> k_optional_sections = {"APPX", "STAR", "RING"};

Error format_error(std::string message) {
    return make_error(ErrorCode::data_format, "data pack: " + std::move(message));
}

// Little-endian reads from a span. Callers check bounds with has() first.
class View {
public:
    View() = default;
    View(std::span<const std::byte> bytes, std::string_view name) : bytes_(bytes), name_(name) {}

    [[nodiscard]] std::size_t size() const noexcept { return bytes_.size(); }
    [[nodiscard]] std::string_view name() const noexcept { return name_; }
    [[nodiscard]] std::span<const std::byte> bytes() const noexcept { return bytes_; }

    [[nodiscard]] bool has(std::size_t offset, std::size_t count) const noexcept {
        return offset <= bytes_.size() && count <= bytes_.size() - offset;
    }

    [[nodiscard]] std::uint64_t uint(std::size_t offset, std::size_t width) const noexcept {
        std::uint64_t value = 0;
        for (std::size_t i = 0; i < width; ++i) {
            value |= std::uint64_t{std::to_integer<std::uint8_t>(bytes_[offset + i])} << (8 * i);
        }
        return value;
    }
    [[nodiscard]] std::uint16_t u16(std::size_t offset) const noexcept { return static_cast<std::uint16_t>(uint(offset, 2)); }
    [[nodiscard]] std::uint32_t u32(std::size_t offset) const noexcept { return static_cast<std::uint32_t>(uint(offset, 4)); }
    [[nodiscard]] std::uint64_t u64(std::size_t offset) const noexcept { return uint(offset, 8); }
    [[nodiscard]] std::int32_t i32(std::size_t offset) const noexcept { return std::bit_cast<std::int32_t>(u32(offset)); }
    [[nodiscard]] std::int64_t i64(std::size_t offset) const noexcept { return std::bit_cast<std::int64_t>(u64(offset)); }
    [[nodiscard]] double f64(std::size_t offset) const noexcept { return std::bit_cast<double>(u64(offset)); }

    template <std::size_t N>
    [[nodiscard]] std::array<std::uint8_t, N> byte_array(std::size_t offset) const noexcept {
        std::array<std::uint8_t, N> out{};
        for (std::size_t i = 0; i < N; ++i) {
            out[i] = std::to_integer<std::uint8_t>(bytes_[offset + i]);
        }
        return out;
    }

    // Copies `count` doubles starting at byte `offset` into `out`.
    void copy_doubles(std::size_t offset, std::size_t count, double* out) const noexcept {
        if constexpr (std::endian::native == std::endian::little) {
            std::memcpy(out, bytes_.data() + offset, count * sizeof(double));
        } else {
            for (std::size_t i = 0; i < count; ++i) {
                out[i] = f64(offset + 8 * i);
            }
        }
    }

private:
    std::span<const std::byte> bytes_;
    std::string_view name_;
};

// Structural UTF-8 check: valid lead and continuation bytes, no overlong forms, no surrogates, at most U+10FFFF.
bool is_valid_utf8(std::string_view text) noexcept {
    std::size_t i = 0;
    while (i < text.size()) {
        const auto lead = static_cast<unsigned char>(text[i]);
        std::size_t length = 0;
        std::uint32_t code = 0;
        if (lead < 0x80) {
            ++i;
            continue;
        }
        if ((lead & 0xE0) == 0xC0) {
            length = 2;
            code = lead & 0x1Fu;
        } else if ((lead & 0xF0) == 0xE0) {
            length = 3;
            code = lead & 0x0Fu;
        } else if ((lead & 0xF8) == 0xF0) {
            length = 4;
            code = lead & 0x07u;
        } else {
            return false;
        }
        if (text.size() - i < length) {
            return false;
        }
        for (std::size_t k = 1; k < length; ++k) {
            const auto next = static_cast<unsigned char>(text[i + k]);
            if ((next & 0xC0) != 0x80) {
                return false;
            }
            code = (code << 6) | (next & 0x3Fu);
        }
        constexpr std::array<std::uint32_t, 5> k_minimum = {0, 0, 0x80, 0x800, 0x10000};
        if (code < k_minimum[length] || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) {
            return false;
        }
        i += length;
    }
    return true;
}

Result<catalog::BodyId> body_id(std::int32_t id_namespace, std::int64_t value, std::string_view what) {
    if (id_namespace < static_cast<std::int32_t>(catalog::IdNamespace::naif) ||
        id_namespace > static_cast<std::int32_t>(catalog::IdNamespace::hr)) {
        return format_error(std::string(what) + ": unknown id namespace " + std::to_string(id_namespace));
    }
    return catalog::BodyId{static_cast<catalog::IdNamespace>(id_namespace), value};
}

Result<frames::FrameId> inertial_frame(std::int32_t code, std::string_view what) {
    switch (code) {
    case static_cast<std::int32_t>(frames::FrameKind::icrf):
        return frames::FrameId::icrf();
    case static_cast<std::int32_t>(frames::FrameKind::eclipj2000):
        return frames::FrameId::eclipj2000();
    default:
        return format_error(std::string(what) + ": unsupported frame code " + std::to_string(code));
    }
}

bool all_finite(std::span<const double> values) noexcept {
    return std::all_of(values.begin(), values.end(), [](double v) { return std::isfinite(v); });
}

// The quantity fields of format 1 (DATA_PACK.md, QNTY): code -> member of Body and required unit.
struct FieldInfo {
    catalog::Quantity catalog::Body::*member;
    catalog::Unit unit;
};

std::optional<FieldInfo> field_info(std::uint32_t code) noexcept {
    switch (code) {
    case 1:
        return FieldInfo{&catalog::Body::gm, catalog::Unit::km3_per_s2};
    case 2:
        return FieldInfo{&catalog::Body::mean_radius, catalog::Unit::km};
    case 3:
        return FieldInfo{&catalog::Body::radius_a, catalog::Unit::km};
    case 4:
        return FieldInfo{&catalog::Body::radius_b, catalog::Unit::km};
    case 5:
        return FieldInfo{&catalog::Body::radius_c, catalog::Unit::km};
    default:
        return std::nullopt;
    }
}

class Parser {
public:
    explicit Parser(std::span<const std::byte> file) : file_(file, "file") {}

    Result<DataPackHeader> header();
    Result<void> sections();
    Result<std::vector<std::pair<std::string, std::string>>> metadata();
    Result<std::vector<catalog::Source>> sources();
    Result<std::vector<SourceFile>> files();
    Result<std::vector<catalog::Body>> bodies();
    Result<void> quantities(std::vector<catalog::Body>& bodies);
    Result<std::vector<frames::IauRotationElements>> rotations(const std::vector<catalog::Body>& bodies);
    Result<void> time_data(std::vector<time::LeapSecondEntry>& leaps, time::TdbModel& model, catalog::SourceRef& source);
    Result<std::vector<EphemerisSegment>> segments(const catalog::BodyCatalog& bodies);
    Result<std::vector<PositionApproximation>> approximations(const catalog::BodyCatalog& bodies,
                                                              const std::vector<EphemerisSegment>& segments);
    Result<catalog::StarCatalog> stars();
    Result<std::vector<catalog::Ring>> rings(const catalog::BodyCatalog& bodies,
                                             const std::vector<frames::IauRotationElements>& rotations);

private:
    struct SectionEntry {
        std::string_view id;
        std::uint64_t offset;
        std::uint64_t size;
    };

    [[nodiscard]] View section(std::string_view id) const {
        for (const SectionEntry& s : sections_) {
            if (s.id == id) {
                return View(file_.bytes().subspan(static_cast<std::size_t>(s.offset), static_cast<std::size_t>(s.size)), id);
            }
        }
        return View(); // only for an absent optional section; sections() checked the required ones
    }

    [[nodiscard]] bool has_section(std::string_view id) const {
        return std::any_of(sections_.begin(), sections_.end(), [&](const SectionEntry& s) { return s.id == id; });
    }

    // A (count, second) pair at the start of a section, and a check that `count` records of `record_size`
    // bytes follow.
    Result<std::uint32_t> record_count(const View& view, std::size_t record_size) const {
        if (!view.has(0, k_counts_size)) {
            return format_error("section " + std::string(view.name()) + " is truncated");
        }
        const std::uint32_t count = view.u32(0);
        if (!view.has(k_counts_size, std::size_t{count} * record_size)) {
            return format_error("section " + std::string(view.name()) + " is truncated");
        }
        return count;
    }

    // The string referenced by the (offset, length) pair at byte `at` of `view`.
    Result<std::string> text(const View& view, std::size_t at) const {
        const std::uint32_t offset = view.u32(at);
        const std::uint32_t length = view.u32(at + 4);
        if (!strings_.has(offset, length)) {
            return format_error("string reference out of bounds in section " + std::string(view.name()));
        }
        std::string value(reinterpret_cast<const char*>(strings_.bytes().data()) + offset, length);
        if (!is_valid_utf8(value)) {
            return format_error("string in section " + std::string(view.name()) + " is not valid UTF-8");
        }
        return value;
    }

    Result<std::string> source_id(std::uint32_t index) const {
        if (index >= source_ids_.size()) {
            return format_error("source index " + std::to_string(index) + " out of range");
        }
        return source_ids_[index];
    }

    View file_;
    View strings_;
    std::vector<SectionEntry> sections_;
    std::vector<std::string> source_ids_;
};

Result<DataPackHeader> Parser::header() {
    const std::span<const std::byte> bytes = file_.bytes();
    const std::string_view start(reinterpret_cast<const char*>(bytes.data()), std::min(bytes.size(), k_lfs_pointer.size()));
    if (start == k_lfs_pointer) {
        return format_error("this is a Git LFS pointer, not the pack; run git lfs pull");
    }
    if (bytes.size() < k_header_size) {
        return format_error("the file is shorter than the header");
    }
    if (std::string_view(reinterpret_cast<const char*>(bytes.data()), k_magic.size()) != k_magic) {
        return format_error("not a data pack (bad magic)");
    }
    if (file_.u32(12) != k_endian_marker) {
        return format_error("bad endianness marker");
    }
    DataPackHeader result;
    result.version = FormatVersion{file_.u16(8), file_.u16(10)};
    if (result.version.major != k_format_version.major) {
        return make_error(ErrorCode::incompatible_version,
                          "data pack: format " + std::to_string(result.version.major) + "." +
                              std::to_string(result.version.minor) + " is not supported (this core reads " +
                              std::to_string(k_format_version.major) + ".x)");
    }
    const std::uint32_t header_size = file_.u32(16);
    result.file_size = file_.u64(24);
    if (header_size < k_header_size || header_size > bytes.size() || result.file_size != bytes.size()) {
        return format_error("header size or file size does not match the file");
    }
    result.content_sha256 = file_.byte_array<32>(32);
    result.lock_sha256 = file_.byte_array<32>(64);
    if (sha256(bytes.subspan(header_size)) != result.content_sha256) {
        return format_error("content hash mismatch (the file is corrupt or was modified)");
    }
    return result;
}

Result<void> Parser::sections() {
    const std::uint32_t header_size = file_.u32(16);
    const std::uint32_t count = file_.u32(20);
    const std::uint64_t table_end = std::uint64_t{header_size} + std::uint64_t{count} * k_section_entry_size;
    if (table_end > file_.size()) {
        return format_error("the section table is truncated");
    }
    const std::uint64_t file_size = file_.size();
    for (std::uint32_t i = 0; i < count; ++i) {
        const std::size_t at = header_size + std::size_t{i} * k_section_entry_size;
        const std::string_view id(reinterpret_cast<const char*>(file_.bytes().data()) + at, 4);
        const std::uint32_t version = file_.u32(at + 4);
        const std::uint64_t offset = file_.u64(at + 8);
        const std::uint64_t size = file_.u64(at + 16);
        if (offset % k_alignment != 0 || offset < table_end || offset > file_size || size > file_size - offset) {
            return format_error("section " + std::string(id) + " is misaligned or out of bounds");
        }
        if (std::any_of(sections_.begin(), sections_.end(), [&](const SectionEntry& s) { return s.id == id; })) {
            return format_error("section " + std::string(id) + " appears twice");
        }
        const bool known = std::find(k_required_sections.begin(), k_required_sections.end(), id) != k_required_sections.end() ||
                           std::find(k_optional_sections.begin(), k_optional_sections.end(), id) != k_optional_sections.end();
        if (known && version != k_section_version) {
            return make_error(ErrorCode::incompatible_version, "data pack: section " + std::string(id) +
                                                                   " has version " + std::to_string(version));
        }
        sections_.push_back({id, offset, size});
    }
    std::vector<SectionEntry> by_offset = sections_;
    std::sort(by_offset.begin(), by_offset.end(), [](const auto& a, const auto& b) { return a.offset < b.offset; });
    for (std::size_t i = 1; i < by_offset.size(); ++i) {
        if (by_offset[i - 1].offset + by_offset[i - 1].size > by_offset[i].offset) {
            return format_error("sections overlap");
        }
    }
    for (const std::string_view id : k_required_sections) {
        if (std::none_of(sections_.begin(), sections_.end(), [&](const SectionEntry& s) { return s.id == id; })) {
            return format_error("missing section " + std::string(id));
        }
    }
    strings_ = section("STRS");
    return {};
}

Result<std::vector<std::pair<std::string, std::string>>> Parser::metadata() {
    const View view = section("META");
    const Result<std::uint32_t> count = record_count(view, k_meta_size);
    if (!count) {
        return count.error();
    }
    std::vector<std::pair<std::string, std::string>> entries;
    for (std::uint32_t i = 0; i < count.value(); ++i) {
        const std::size_t at = k_counts_size + std::size_t{i} * k_meta_size;
        Result<std::string> key = text(view, at);
        Result<std::string> value = text(view, at + 8);
        if (!key || !value) {
            return key ? value.error() : key.error();
        }
        entries.emplace_back(std::move(key).value(), std::move(value).value());
    }
    std::sort(entries.begin(), entries.end());
    for (std::size_t i = 1; i < entries.size(); ++i) {
        if (entries[i - 1].first == entries[i].first) {
            return format_error("metadata key " + entries[i].first + " appears twice");
        }
    }
    return entries;
}

Result<std::vector<catalog::Source>> Parser::sources() {
    const View view = section("SRCS");
    const Result<std::uint32_t> count = record_count(view, k_source_size);
    if (!count) {
        return count.error();
    }
    std::vector<catalog::Source> sources;
    for (std::uint32_t i = 0; i < count.value(); ++i) {
        const std::size_t at = k_counts_size + std::size_t{i} * k_source_size;
        std::array<std::string, 6> fields;
        for (std::size_t f = 0; f < fields.size(); ++f) {
            Result<std::string> value = text(view, at + 8 * f);
            if (!value) {
                return value.error();
            }
            fields[f] = std::move(value).value();
        }
        sources.push_back(catalog::Source{fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], view.i32(at + 48)});
        source_ids_.push_back(fields[0]);
    }
    return sources;
}

Result<std::vector<SourceFile>> Parser::files() {
    const View view = section("FILE");
    const Result<std::uint32_t> count = record_count(view, k_file_size);
    if (!count) {
        return count.error();
    }
    std::vector<SourceFile> files;
    for (std::uint32_t i = 0; i < count.value(); ++i) {
        const std::size_t at = k_counts_size + std::size_t{i} * k_file_size;
        Result<std::string> source = source_id(view.u32(at));
        Result<std::string> name = text(view, at + 8);
        Result<std::string> url = text(view, at + 16);
        Result<std::string> retrieved = text(view, at + 24);
        for (const Result<std::string>* r : {&source, &name, &url, &retrieved}) {
            if (!*r) {
                return r->error();
            }
        }
        SourceFile file;
        file.source_id = std::move(source).value();
        file.name = std::move(name).value();
        file.url = std::move(url).value();
        file.retrieved_utc = std::move(retrieved).value();
        file.size_bytes = view.u64(at + 32);
        file.sha256 = view.byte_array<32>(at + 40);
        file.md5 = view.byte_array<16>(at + 72);
        file.publisher_checksum_verified = (view.u32(at + 4) & k_file_publisher_verified) != 0;
        files.push_back(std::move(file));
    }
    return files;
}

Result<std::vector<catalog::Body>> Parser::bodies() {
    const View view = section("BODY");
    const Result<std::uint32_t> count = record_count(view, k_body_size);
    if (!count) {
        return count.error();
    }
    std::vector<catalog::Body> bodies;
    for (std::uint32_t i = 0; i < count.value(); ++i) {
        const std::size_t at = k_counts_size + std::size_t{i} * k_body_size;
        const Result<catalog::BodyId> id = body_id(view.i32(at), view.i64(at + 8), "body");
        if (!id) {
            return id.error();
        }
        const std::int32_t kind = view.i32(at + 4);
        if (kind < 0 || kind > static_cast<std::int32_t>(catalog::BodyKind::galaxy)) {
            return format_error("body " + catalog::to_string(id.value()) + ": unknown kind " + std::to_string(kind));
        }
        Result<std::string> name = text(view, at + 32);
        if (!name) {
            return name.error();
        }
        catalog::Body body;
        body.id = id.value();
        body.name = std::move(name).value();
        body.kind = static_cast<catalog::BodyKind>(kind);
        if (view.i32(at + 16) != 0) {
            const Result<catalog::BodyId> primary = body_id(view.i32(at + 16), view.i64(at + 24), "primary");
            if (!primary) {
                return primary.error();
            }
            body.primary = primary.value();
        }
        bodies.push_back(std::move(body));
    }
    return bodies;
}

Result<void> Parser::quantities(std::vector<catalog::Body>& bodies) {
    const View view = section("QNTY");
    const Result<std::uint32_t> count = record_count(view, k_quantity_size);
    if (!count) {
        return count.error();
    }
    std::vector<std::uint32_t> seen(bodies.size(), 0); // bit per field code
    for (std::uint32_t i = 0; i < count.value(); ++i) {
        const std::size_t at = k_counts_size + std::size_t{i} * k_quantity_size;
        const std::uint32_t index = view.u32(at);
        const std::uint32_t field = view.u32(at + 4);
        const std::uint32_t unit = view.u32(at + 8);
        const std::uint32_t flags = view.u32(at + 12);
        if (index >= bodies.size()) {
            return format_error("quantity " + std::to_string(i) + ": body index out of range");
        }
        const std::optional<FieldInfo> info = field_info(field);
        const std::string what = "quantity " + std::to_string(field) + " of " + catalog::to_string(bodies[index].id);
        if (!info) {
            return format_error(what + ": unknown field code");
        }
        if (unit != static_cast<std::uint32_t>(info->unit)) {
            return format_error(what + ": unit " + std::to_string(unit) + " does not match the field");
        }
        if ((seen[index] >> field) & 1u) {
            return format_error(what + " appears twice");
        }
        seen[index] |= 1u << field;
        Result<std::string> source = source_id(view.u32(at + 32));
        Result<std::string> locator = text(view, at + 40);
        Result<std::string> derivation = text(view, at + 48);
        for (const Result<std::string>* r : {&source, &locator, &derivation}) {
            if (!*r) {
                return r->error();
            }
        }
        catalog::Quantity quantity;
        quantity.value = view.f64(at + 16);
        quantity.unit = info->unit;
        if ((flags & k_quantity_has_uncertainty) != 0) {
            quantity.uncertainty = view.f64(at + 24);
        }
        quantity.source = catalog::SourceRef{std::move(source).value(), std::move(locator).value()};
        quantity.derivation = std::move(derivation).value();
        bodies[index].*(info->member) = std::move(quantity);
    }
    return {};
}

Result<std::vector<frames::IauRotationElements>> Parser::rotations(const std::vector<catalog::Body>& bodies) {
    const View view = section("ROTN");
    const Result<std::uint32_t> count = record_count(view, k_rotation_size);
    if (!count) {
        return count.error();
    }
    const std::uint32_t pool_count = view.u32(4);
    const std::size_t pool_start = k_counts_size + std::size_t{count.value()} * k_rotation_size;
    if (!view.has(pool_start, std::size_t{pool_count} * 8)) {
        return format_error("section ROTN is truncated");
    }
    std::vector<double> pool(pool_count);
    view.copy_doubles(pool_start, pool_count, pool.data());
    if (!all_finite(pool)) {
        return format_error("rotation elements: non-finite coefficient");
    }

    std::vector<frames::IauRotationElements> rotations;
    for (std::uint32_t i = 0; i < count.value(); ++i) {
        const std::size_t at = k_counts_size + std::size_t{i} * k_rotation_size;
        const std::uint32_t index = view.u32(at);
        if (index >= bodies.size()) {
            return format_error("rotation " + std::to_string(i) + ": body index out of range");
        }
        frames::IauRotationElements r;
        r.body = bodies[index].id;
        const std::string what = "rotation of " + catalog::to_string(r.body);
        if (std::any_of(rotations.begin(), rotations.end(), [&](const auto& other) { return other.body == r.body; })) {
            return format_error(what + " appears twice");
        }
        const Result<frames::FrameId> frame = inertial_frame(view.i32(at + 4), what);
        Result<std::string> source = source_id(view.u32(at + 8));
        Result<std::string> locator = text(view, at + 80);
        if (!frame || !source || !locator) {
            return !frame ? frame.error() : (!source ? source.error() : locator.error());
        }
        r.frame = frame.value();
        r.source = catalog::SourceRef{std::move(source).value(), std::move(locator).value()};
        const std::uint32_t degree = view.u32(at + 12);
        if (degree < 1 || degree > k_max_phase_degree) {
            return format_error(what + ": phase angle degree " + std::to_string(degree) + " is not supported");
        }
        r.phase_degree = static_cast<std::int32_t>(degree);

        std::array<std::vector<double>*, 7> arrays = {&r.pole_ra_deg,     &r.pole_dec_deg,     &r.pm_deg,
                                                       &r.nut_prec_ra_deg, &r.nut_prec_dec_deg, &r.nut_prec_pm_deg,
                                                       &r.nut_prec_angles_deg};
        for (std::size_t a = 0; a < arrays.size(); ++a) {
            const std::uint32_t first = view.u32(at + 24 + 8 * a);
            const std::uint32_t length = view.u32(at + 28 + 8 * a);
            if (first > pool_count || length > pool_count - first) {
                return format_error(what + ": coefficient array out of range");
            }
            arrays[a]->assign(pool.begin() + first, pool.begin() + first + length);
        }
        for (const std::vector<double>* polynomial : {&r.pole_ra_deg, &r.pole_dec_deg, &r.pm_deg}) {
            if (polynomial->empty() || polynomial->size() > 3) {
                return format_error(what + ": a pole or prime meridian polynomial needs 1 to 3 coefficients");
            }
        }
        if (r.nut_prec_angles_deg.size() % (degree + 1) != 0) {
            return format_error(what + ": phase angle table does not match degree " + std::to_string(degree));
        }
        const std::size_t angles = r.angle_count();
        if (r.nut_prec_ra_deg.size() > angles || r.nut_prec_dec_deg.size() > angles || r.nut_prec_pm_deg.size() > angles) {
            return format_error(what + ": more nutation terms than phase angles");
        }
        if (angles > 0) {
            const Result<catalog::BodyId> angles_body =
                body_id(static_cast<std::int32_t>(catalog::IdNamespace::naif), view.i64(at + 16), what);
            if (!angles_body) {
                return angles_body.error();
            }
            r.angles_body = angles_body.value();
        }
        rotations.push_back(std::move(r));
    }
    return rotations;
}

Result<void> Parser::time_data(std::vector<time::LeapSecondEntry>& leaps, time::TdbModel& model,
                               catalog::SourceRef& source) {
    const View view = section("TIME");
    if (!view.has(0, k_time_size)) {
        return format_error("section TIME is truncated");
    }
    const std::uint32_t count = view.u32(0);
    if (!view.has(k_time_size, std::size_t{count} * k_leap_size)) {
        return format_error("section TIME is truncated");
    }
    Result<std::string> source_id_text = source_id(view.u32(4));
    Result<std::string> locator = text(view, 48);
    if (!source_id_text || !locator) {
        return !source_id_text ? source_id_text.error() : locator.error();
    }
    source = catalog::SourceRef{std::move(source_id_text).value(), std::move(locator).value()};
    const double tt_minus_tai_s = view.f64(8);
    if (tt_minus_tai_s != constants::k_tt_minus_tai_s) {
        return format_error("TT - TAI in the pack differs from the definitional 32.184 s");
    }
    model = time::TdbModel{view.f64(16), view.f64(24), view.f64(32), view.f64(40)};
    if (!std::isfinite(model.k_s) || !std::isfinite(model.eb) || !std::isfinite(model.m0_rad) ||
        !std::isfinite(model.m1_rad_per_s)) {
        return format_error("TDB - TT model parameters are not finite");
    }
    for (std::uint32_t i = 0; i < count; ++i) {
        const std::size_t at = k_time_size + std::size_t{i} * k_leap_size;
        leaps.push_back(time::LeapSecondEntry{time::CalendarDate{view.i32(at), view.i32(at + 4), view.i32(at + 8)},
                                              view.i32(at + 12)});
    }
    return {};
}

Result<std::vector<EphemerisSegment>> Parser::segments(const catalog::BodyCatalog& bodies) {
    const View data = section("EPHD");
    if (data.size() % 8 != 0) {
        return format_error("section EPHD is not a whole number of doubles");
    }
    const std::uint64_t data_count = data.size() / 8;
    const View view = section("EPHS");
    const Result<std::uint32_t> count = record_count(view, k_segment_size);
    if (!count) {
        return count.error();
    }
    std::vector<EphemerisSegment> segments;
    for (std::uint32_t i = 0; i < count.value(); ++i) {
        const std::size_t at = k_counts_size + std::size_t{i} * k_segment_size;
        const Result<catalog::BodyId> target = body_id(view.i32(at), view.i64(at + 8), "segment target");
        const Result<catalog::BodyId> center = body_id(view.i32(at + 4), view.i64(at + 16), "segment center");
        if (!target || !center) {
            return !target ? target.error() : center.error();
        }
        const std::string what = "segment " + std::to_string(i) + " (" + catalog::to_string(target.value()) +
                                 " wrt " + catalog::to_string(center.value()) + ")";
        if (bodies.find(target.value()) == nullptr || bodies.find(center.value()) == nullptr) {
            return format_error(what + ": target or center is not in the catalog");
        }
        const Result<frames::FrameId> frame = inertial_frame(view.i32(at + 24), what);
        Result<std::string> source = source_id(view.u32(at + 80));
        Result<std::string> locator = text(view, at + 88);
        if (!frame || !source || !locator) {
            return !frame ? frame.error() : (!source ? source.error() : locator.error());
        }

        EphemerisSegment s;
        s.target = target.value();
        s.center = center.value();
        s.frame = frame.value();
        s.spk_type = view.i32(at + 28);
        s.start_tdb_s = view.f64(at + 32);
        s.end_tdb_s = view.f64(at + 40);
        s.init_tdb_s = view.f64(at + 48);
        s.interval_s = view.f64(at + 56);
        s.record_size = view.u32(at + 64);
        s.record_count = view.u32(at + 68);
        s.source = catalog::SourceRef{std::move(source).value(), std::move(locator).value()};
        const std::uint64_t first = view.u64(at + 72);

        if (s.spk_type != 2 && s.spk_type != 3) {
            return format_error(what + ": SPK type " + std::to_string(s.spk_type) + " is not supported");
        }
        const std::uint32_t components = s.spk_type == 2 ? 3u : 6u;
        if (s.record_size < 2 + components || (s.record_size - 2) % components != 0 || s.record_count == 0) {
            return format_error(what + ": record layout does not fit SPK type " + std::to_string(s.spk_type));
        }
        const double records_end = s.init_tdb_s + static_cast<double>(s.record_count) * s.interval_s;
        if (!std::isfinite(s.interval_s) || !(s.interval_s > 0.0) || !std::isfinite(s.init_tdb_s) ||
            !(s.init_tdb_s <= s.start_tdb_s) || !(s.start_tdb_s < s.end_tdb_s) || !(s.end_tdb_s <= records_end)) {
            return format_error(what + ": invalid coverage or record interval");
        }
        const std::uint64_t doubles = std::uint64_t{s.record_size} * s.record_count;
        if (first > data_count || doubles > data_count - first) {
            return format_error(what + ": records out of range");
        }
        s.records.resize(static_cast<std::size_t>(doubles));
        data.copy_doubles(static_cast<std::size_t>(first) * 8, s.records.size(), s.records.data());
        if (!all_finite(s.records)) {
            return format_error(what + ": non-finite coefficient");
        }
        segments.push_back(std::move(s));
    }
    return segments;
}

Result<std::vector<PositionApproximation>> Parser::approximations(const catalog::BodyCatalog& bodies,
                                                                  const std::vector<EphemerisSegment>& segments) {
    std::vector<PositionApproximation> result;
    if (!has_section("APPX")) {
        return result; // a format 1.0 pack
    }
    const View view = section("APPX");
    const Result<std::uint32_t> count = record_count(view, k_approximation_size);
    if (!count) {
        return count.error();
    }
    for (std::uint32_t i = 0; i < count.value(); ++i) {
        const std::size_t at = k_counts_size + std::size_t{i} * k_approximation_size;
        const Result<catalog::BodyId> body = body_id(view.i32(at), view.i64(at + 8), "approximated body");
        const Result<catalog::BodyId> proxy = body_id(view.i32(at + 4), view.i64(at + 16), "approximation proxy");
        if (!body || !proxy) {
            return !body ? body.error() : proxy.error();
        }
        const std::string what = "approximate position of " + catalog::to_string(body.value());
        const catalog::Body* entry = bodies.find(body.value());
        if (entry == nullptr) {
            return format_error(what + ": the body is not in the catalog");
        }
        if (!entry->primary || *entry->primary != proxy.value()) {
            return format_error(what + ": the proxy is not the body's primary");
        }
        if (std::any_of(segments.begin(), segments.end(), [&](const EphemerisSegment& s) { return s.target == body.value(); })) {
            return format_error(what + ": the body has its own ephemeris segment");
        }
        if (std::any_of(result.begin(), result.end(), [&](const PositionApproximation& a) { return a.body == body.value(); })) {
            return format_error(what + " appears twice");
        }
        Result<std::string> reason = text(view, at + 24);
        if (!reason) {
            return reason.error();
        }
        result.push_back({body.value(), proxy.value(), std::move(reason).value()});
    }
    return result;
}

Result<catalog::StarCatalog> Parser::stars() {
    catalog::StarCatalog result;
    if (!has_section("STAR")) {
        return result; // no star catalog
    }
    const View view = section("STAR");
    if (!view.has(0, k_star_header_size)) {
        return format_error("section STAR is truncated");
    }
    const std::uint32_t count = view.u32(0);
    if (!view.has(k_star_header_size, std::size_t{count} * k_star_size)) {
        return format_error("section STAR is truncated");
    }
    Result<std::string> source = source_id(view.u32(4));
    Result<std::string> locator = text(view, 16);
    Result<std::string> derivation = text(view, 24);
    if (!source || !locator || !derivation) {
        return !source ? source.error() : (!locator ? locator.error() : derivation.error());
    }
    result.epoch_tdb_s = view.f64(8);
    if (!std::isfinite(result.epoch_tdb_s) || count == 0) {
        return format_error("star catalog: invalid epoch or no stars");
    }
    result.source = catalog::SourceRef{std::move(source).value(), std::move(locator).value()};
    result.derivation = std::move(derivation).value();
    result.stars.reserve(count);
    for (std::uint32_t i = 0; i < count; ++i) {
        const std::size_t at = k_star_header_size + std::size_t{i} * k_star_size;
        const Result<catalog::BodyId> id = body_id(view.i32(at), view.i64(at + 8), "star");
        Result<std::string> designation = text(view, at + 64);
        if (!id || !designation) {
            return !id ? id.error() : designation.error();
        }
        const std::uint32_t flags = view.u32(at + 4);
        catalog::Star star;
        star.id = id.value();
        star.designation = std::move(designation).value();
        star.ra_rad = view.f64(at + 16);
        star.dec_rad = view.f64(at + 24);
        star.v_magnitude = view.f64(at + 48);
        const std::array<double, 5> values = {star.ra_rad, star.dec_rad, view.f64(at + 32), view.f64(at + 40),
                                              view.f64(at + 56)};
        const std::string what = "star " + catalog::to_string(star.id);
        if (!all_finite(values) || !std::isfinite(star.v_magnitude) || !(star.ra_rad >= 0.0) ||
            !(star.ra_rad < 2.0 * k_pi) || !(std::abs(star.dec_rad) <= k_pi / 2.0)) {
            return format_error(what + ": invalid direction or value");
        }
        if ((flags & k_star_has_proper_motion) != 0) {
            star.proper_motion = catalog::ProperMotion{values[2], values[3]};
        }
        if ((flags & k_star_has_color) != 0) {
            star.b_minus_v = values[4];
        }
        if (!result.stars.empty() && !(std::pair(result.stars.back().id.id_namespace, result.stars.back().id.value) <
                                       std::pair(star.id.id_namespace, star.id.value))) {
            return format_error(what + ": stars are not sorted by id, or a star appears twice");
        }
        result.stars.push_back(std::move(star));
    }
    return result;
}

Result<std::vector<catalog::Ring>> Parser::rings(const catalog::BodyCatalog& bodies,
                                                 const std::vector<frames::IauRotationElements>& rotations) {
    std::vector<catalog::Ring> result;
    if (!has_section("RING")) {
        return result; // no rings
    }
    const View view = section("RING");
    const Result<std::uint32_t> count = record_count(view, k_ring_size);
    if (!count) {
        return count.error();
    }
    std::uint32_t previous_index = 0;
    for (std::uint32_t i = 0; i < count.value(); ++i) {
        const std::size_t at = k_counts_size + std::size_t{i} * k_ring_size;
        const std::uint32_t index = view.u32(at);
        if (index >= bodies.bodies().size()) {
            return format_error("ring " + std::to_string(i) + ": body index out of range");
        }
        catalog::Ring ring;
        ring.body = bodies.bodies()[index].id;
        const std::uint32_t flags = view.u32(at + 4);
        ring.gap = (flags & k_ring_gap) != 0;
        ring.inner_radius_km = view.f64(at + 8);
        ring.outer_radius_km = view.f64(at + 16);
        Result<std::string> name = text(view, at + 48);
        Result<std::string> radii_source = source_id(view.u32(at + 40));
        Result<std::string> radii_locator = text(view, at + 56);
        if (!name || !radii_source || !radii_locator) {
            return !name ? name.error() : (!radii_source ? radii_source.error() : radii_locator.error());
        }
        ring.name = std::move(name).value();
        ring.radii_source = catalog::SourceRef{std::move(radii_source).value(), std::move(radii_locator).value()};
        const std::string what = "ring " + ring.name + " of " + catalog::to_string(ring.body);
        if (ring.name.empty() || !std::isfinite(ring.inner_radius_km) || !std::isfinite(ring.outer_radius_km) ||
            !(ring.inner_radius_km > 0.0) || !(ring.inner_radius_km < ring.outer_radius_km)) {
            return format_error(what + ": needs a name and radii with 0 < inner < outer");
        }
        if (std::none_of(rotations.begin(), rotations.end(), [&](const auto& r) { return r.body == ring.body; })) {
            return format_error(what + ": the body has no rotation model to define the ring plane");
        }
        if ((flags & k_ring_has_optical_depth) != 0) {
            const double smallest = view.f64(at + 24);
            const double largest = view.f64(at + 32);
            Result<std::string> depth_source = source_id(view.u32(at + 44));
            Result<std::string> depth_locator = text(view, at + 64);
            if (!depth_source || !depth_locator) {
                return !depth_source ? depth_source.error() : depth_locator.error();
            }
            if (ring.gap || !std::isfinite(smallest) || !std::isfinite(largest) || !(smallest >= 0.0) ||
                !(smallest <= largest)) {
                return format_error(what + ": invalid optical depth (or one given for a gap)");
            }
            ring.optical_depth = catalog::OpticalDepth{
                smallest, largest, catalog::SourceRef{std::move(depth_source).value(), std::move(depth_locator).value()}};
        }
        if (!result.empty() && (previous_index > index || (previous_index == index &&
                                                             result.back().inner_radius_km > ring.inner_radius_km))) {
            return format_error(what + ": rings are not sorted by body and inner radius");
        }
        previous_index = index;
        result.push_back(std::move(ring));
    }
    return result;
}

} // namespace

DataPack::DataPack(DataPackHeader header, std::vector<std::pair<std::string, std::string>> metadata,
                   catalog::BodyCatalog catalog, std::vector<SourceFile> files, time::LeapSecondTable leap_seconds,
                   time::TdbModel tdb_model, catalog::SourceRef time_source,
                   std::vector<frames::IauRotationElements> rotations, std::vector<EphemerisSegment> segments,
                   std::vector<PositionApproximation> approximations, catalog::StarCatalog stars,
                   std::vector<catalog::Ring> rings)
    : header_(header), metadata_(std::move(metadata)), catalog_(std::move(catalog)), files_(std::move(files)),
      leap_seconds_(std::move(leap_seconds)), tdb_model_(tdb_model), time_source_(std::move(time_source)),
      rotations_(std::move(rotations)), segments_(std::move(segments)), approximations_(std::move(approximations)),
      stars_(std::move(stars)), rings_(std::move(rings)) {}

Result<DataPack> DataPack::parse(std::span<const std::byte> bytes) {
    Parser parser(bytes);
    Result<DataPackHeader> header = parser.header();
    if (!header) {
        return header.error();
    }
    if (Result<void> sections = parser.sections(); !sections) {
        return sections.error();
    }
    Result<std::vector<std::pair<std::string, std::string>>> metadata = parser.metadata();
    if (!metadata) {
        return metadata.error();
    }
    Result<std::vector<catalog::Source>> sources = parser.sources();
    if (!sources) {
        return sources.error();
    }
    Result<std::vector<SourceFile>> files = parser.files();
    if (!files) {
        return files.error();
    }
    Result<std::vector<catalog::Body>> bodies = parser.bodies();
    if (!bodies) {
        return bodies.error();
    }
    if (Result<void> q = parser.quantities(bodies.value()); !q) {
        return q.error();
    }
    Result<std::vector<frames::IauRotationElements>> rotations = parser.rotations(bodies.value());
    if (!rotations) {
        return rotations.error();
    }
    std::vector<time::LeapSecondEntry> leaps;
    time::TdbModel model{};
    catalog::SourceRef time_source;
    if (Result<void> t = parser.time_data(leaps, model, time_source); !t) {
        return t.error();
    }

    Result<catalog::BodyCatalog> bodies_catalog = catalog::BodyCatalog::create(std::move(sources).value(), std::move(bodies).value());
    if (!bodies_catalog) {
        return format_error("catalog: " + bodies_catalog.error().message);
    }
    for (const SourceFile& file : files.value()) {
        if (bodies_catalog.value().find_source(file.source_id) == nullptr) {
            return format_error("file " + file.name + " cites an unknown source");
        }
    }
    for (const frames::IauRotationElements& r : rotations.value()) {
        if (bodies_catalog.value().find_source(r.source.source_id) == nullptr) {
            return format_error("rotation of " + catalog::to_string(r.body) + " cites an unknown source");
        }
    }
    Result<time::LeapSecondTable> leap_seconds = time::LeapSecondTable::create(leaps);
    if (!leap_seconds) {
        return format_error("leap second table: " + leap_seconds.error().message);
    }
    Result<std::vector<EphemerisSegment>> segments = parser.segments(bodies_catalog.value());
    if (!segments) {
        return segments.error();
    }
    Result<std::vector<PositionApproximation>> approximations = parser.approximations(bodies_catalog.value(), segments.value());
    if (!approximations) {
        return approximations.error();
    }
    Result<catalog::StarCatalog> stars = parser.stars();
    if (!stars) {
        return stars.error();
    }
    Result<std::vector<catalog::Ring>> rings = parser.rings(bodies_catalog.value(), rotations.value());
    if (!rings) {
        return rings.error();
    }
    return DataPack(header.value(), std::move(metadata).value(), std::move(bodies_catalog).value(), std::move(files).value(),
                    std::move(leap_seconds).value(), model, std::move(time_source), std::move(rotations).value(),
                    std::move(segments).value(), std::move(approximations).value(), std::move(stars).value(),
                    std::move(rings).value());
}

Result<DataPack> DataPack::load_file(const std::filesystem::path& path) {
    std::ifstream in(path, std::ios::binary | std::ios::ate);
    if (!in) {
        return make_error(ErrorCode::io_error, "cannot open " + path.string());
    }
    const std::streamoff size = in.tellg();
    if (size < 0 || static_cast<std::uint64_t>(size) > std::numeric_limits<std::size_t>::max()) {
        return make_error(ErrorCode::io_error, "cannot determine the size of " + path.string());
    }
    std::vector<std::byte> bytes(static_cast<std::size_t>(size));
    in.seekg(0);
    if (!in.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(size))) {
        return make_error(ErrorCode::io_error, "cannot read " + path.string());
    }
    return parse(bytes);
}

std::optional<std::string_view> DataPack::metadata_value(std::string_view key) const noexcept {
    const auto it = std::lower_bound(metadata_.begin(), metadata_.end(), key,
                                     [](const auto& entry, std::string_view k) { return entry.first < k; });
    if (it == metadata_.end() || it->first != key) {
        return std::nullopt;
    }
    return std::string_view(it->second);
}

const frames::IauRotationElements* DataPack::find_rotation(catalog::BodyId body) const noexcept {
    const auto it = std::find_if(rotations_.begin(), rotations_.end(), [&](const auto& r) { return r.body == body; });
    return it == rotations_.end() ? nullptr : &*it;
}

} // namespace usim::data
