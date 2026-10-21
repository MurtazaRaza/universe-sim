#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>

namespace usim::catalog {

/// A registered data source (an entry of data/sources.json, described in docs/DATA_SOURCES.md).
struct Source {
    std::string id;       ///< Registry id, for example "S-NAIF-PCK".
    std::string title;    ///< Human-readable name of the dataset.
    std::string provider; ///< Publishing organization, for example "NASA/JPL NAIF".
    std::string url;      ///< Where the data was retrieved from.
    std::string citation; ///< Reference to cite for the data.
    std::string license;  ///< License or terms of use.
    std::int32_t tier = 0; ///< Trust tier from DATA_SOURCES.md (1 is the most authoritative).
};

/// Where one value came from: a registered source and a locator inside it.
struct SourceRef {
    std::string source_id; ///< A Source::id. Empty only for a missing value with no source.
    std::string locator;   ///< File and keyword, or table and row, for example "pck00011.tpc BODY399_RADII[0]".
};

/// The unit of a Quantity. Always one of the internal units (SPEC section 7.1); display units are applied
/// in presentation code.
enum class Unit : std::uint8_t {
    none,       ///< Dimensionless.
    km,         ///< Kilometers.
    s,          ///< Seconds.
    km_per_s,   ///< Kilometers per second.
    rad,        ///< Radians.
    rad_per_s,  ///< Radians per second.
    km3_per_s2, ///< Gravitational parameter, km^3/s^2.
};

/// Unit symbol: "", "km", "s", "km/s", "rad", "rad/s" or "km^3/s^2".
[[nodiscard]] std::string_view to_string(Unit unit) noexcept;

/// A physical value with its provenance (SPEC section 10). A missing value is explicit (no value),
/// never 0 or NaN (FR-DATA-05).
struct Quantity {
    std::optional<double> value;       ///< The value in `unit`, or nothing if no source provides it.
    Unit unit = Unit::none;            ///< Unit of value and uncertainty.
    std::optional<double> uncertainty; ///< One-sigma uncertainty in `unit`, when the source gives one.
    SourceRef source;                  ///< Where the value came from.
    std::string derivation;            ///< For derived values: the formula and its inputs. Empty otherwise.

    /// A missing value of the given unit.
    [[nodiscard]] static Quantity missing(Unit unit) { return Quantity{std::nullopt, unit, std::nullopt, {}, {}}; }

    /// True if a value is present.
    [[nodiscard]] bool has_value() const noexcept { return value.has_value(); }

    /// True if the value was computed by the pipeline from other values (FR-DATA-04).
    [[nodiscard]] bool is_derived() const noexcept { return !derivation.empty(); }
};

} // namespace usim::catalog
