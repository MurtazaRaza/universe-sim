#pragma once

// Helpers for validation tests: locating the default pack and reading the reference CSV files in
// data/reference/ (comment lines start with '#'; the first other line names the columns).

#include "usim/data/data_pack.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <cctype>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <map>
#include <optional>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

namespace usim::test::validation {

inline std::filesystem::path data_dir() {
    return std::filesystem::path(USIM_DATA_DIR);
}

/// The newest data/packs/<prefix><version>.uspack, if one exists and is not a Git LFS pointer. The version
/// must start with a digit, so the prefix "solar-system-" does not match solar-system-moons-*.uspack.
inline std::optional<std::filesystem::path> newest_pack_path(std::string_view prefix) {
    std::vector<std::filesystem::path> found;
    const auto packs = data_dir() / "packs";
    if (std::filesystem::is_directory(packs)) {
        for (const auto& entry : std::filesystem::directory_iterator(packs)) {
            const std::string name = entry.path().filename().string();
            if (name.starts_with(prefix) && name.size() > prefix.size() &&
                std::isdigit(static_cast<unsigned char>(name[prefix.size()])) != 0 &&
                name.ends_with(".uspack")) {
                found.push_back(entry.path());
            }
        }
    }
    std::sort(found.begin(), found.end());
    if (found.empty()) {
        return std::nullopt;
    }
    std::ifstream in(found.back(), std::ios::binary);
    std::string start(23, '\0');
    in.read(start.data(), static_cast<std::streamsize>(start.size()));
    if (start == "version https://git-lfs") {
        return std::nullopt;
    }
    return found.back();
}

/// The newest default pack, data/packs/solar-system-<version>.uspack.
inline std::optional<std::filesystem::path> default_pack_path() {
    return newest_pack_path("solar-system-");
}

/// The newest optional moons pack, data/packs/solar-system-moons-<version>.uspack (P6-01).
inline std::optional<std::filesystem::path> moons_pack_path() {
    return newest_pack_path("solar-system-moons-");
}

inline std::optional<data::DataPack> load_pack(const std::optional<std::filesystem::path>& path) {
    if (!path) {
        return std::nullopt;
    }
    auto loaded = data::DataPack::load_file(*path);
    if (!loaded) {
        FAIL("cannot load " << path->string() << ": " << loaded.error().message);
    }
    return std::move(loaded).value();
}

/// The default pack, loaded once. Skips the calling test if it has not been built (or pulled).
inline const data::DataPack& default_pack() {
    static const std::optional<data::DataPack> pack = load_pack(default_pack_path());
    if (!pack) {
        SKIP("no default pack in data/packs (run tools/pipeline/usim-pipeline build, or git lfs pull)");
    }
    return *pack;
}

/// The optional moons pack, loaded once. Skips the calling test if it has not been built (it needs the
/// satellite kernels: usim-pipeline fetch --optional, then build --pack solar-system-moons).
inline const data::DataPack& moons_pack() {
    static const std::optional<data::DataPack> pack = load_pack(moons_pack_path());
    if (!pack) {
        SKIP("no moons pack in data/packs (usim-pipeline fetch --optional, then build --pack solar-system-moons)");
    }
    return *pack;
}

/// A reference CSV file.
struct Table {
    std::vector<std::string> columns;
    std::vector<std::vector<std::string>> rows;

    [[nodiscard]] std::size_t column(std::string_view name) const {
        const auto it = std::find(columns.begin(), columns.end(), name);
        REQUIRE(it != columns.end());
        return static_cast<std::size_t>(it - columns.begin());
    }
};

inline std::vector<std::string> split(const std::string& line) {
    std::vector<std::string> fields;
    std::stringstream stream(line);
    std::string field;
    while (std::getline(stream, field, ',')) {
        fields.push_back(field);
    }
    return fields;
}

/// Reads data/reference/<name>. Fails the test if the file is missing or malformed.
inline Table read_reference(const std::string& name) {
    std::ifstream in(data_dir() / "reference" / name);
    REQUIRE(in.good());
    Table table;
    std::string line;
    while (std::getline(in, line)) {
        if (line.empty() || line.front() == '#') {
            continue;
        }
        auto fields = split(line);
        if (table.columns.empty()) {
            table.columns = std::move(fields);
        } else {
            REQUIRE(fields.size() == table.columns.size());
            table.rows.push_back(std::move(fields));
        }
    }
    REQUIRE_FALSE(table.rows.empty());
    return table;
}

/// A decimal number as written in a reference file or a NAIF text kernel (D or E exponent), parsed with
/// strtod, which rounds correctly.
inline double parse_double(std::string text) {
    std::replace(text.begin(), text.end(), 'D', 'E');
    std::replace(text.begin(), text.end(), 'd', 'e');
    char* end = nullptr;
    const double value = std::strtod(text.c_str(), &end);
    REQUIRE(end == text.c_str() + text.size());
    return value;
}

} // namespace usim::test::validation
