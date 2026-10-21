#pragma once

#include <cstdint>
#include <string_view>

namespace usim::time {

/// Astronomical time scales the core converts between (SPEC section 7.2).
enum class TimeScale : std::uint8_t {
    tdb, ///< Barycentric Dynamical Time: the simulation's internal scale and the JPL ephemeris argument.
    tt,  ///< Terrestrial Time.
    tai, ///< International Atomic Time.
    utc, ///< Coordinated Universal Time (with leap seconds; defined from 1972-01-01 only).
};

/// The scale's abbreviation as used in time strings: "TDB", "TT", "TAI" or "UTC".
[[nodiscard]] constexpr std::string_view to_string(TimeScale scale) noexcept {
    switch (scale) {
    case TimeScale::tdb:
        return "TDB";
    case TimeScale::tt:
        return "TT";
    case TimeScale::tai:
        return "TAI";
    case TimeScale::utc:
        return "UTC";
    }
    return "?";
}

} // namespace usim::time
