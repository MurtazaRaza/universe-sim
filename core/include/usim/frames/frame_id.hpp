#pragma once

#include "usim/catalog/body_id.hpp"

#include <cstdint>
#include <string>

namespace usim::frames {

/// The kind of a reference frame (SPEC section 7.3). Values are stable and shared with the C ABI.
enum class FrameKind : std::int32_t {
    icrf = 0,       ///< International Celestial Reference Frame (SPICE "J2000"): the core's native frame.
    eclipj2000 = 1, ///< Mean ecliptic and equinox of J2000 (SPICE "ECLIPJ2000"): the default display frame.
    body_fixed = 2, ///< A body's IAU body-fixed frame (SPICE "IAU_<body>"). Available from P4-03.
};

/// Identifies a reference frame: its kind and, for body-fixed frames, the body.
class FrameId {
public:
    /// The ICRF.
    [[nodiscard]] static constexpr FrameId icrf() noexcept { return FrameId(FrameKind::icrf, {}); }

    /// ECLIPJ2000.
    [[nodiscard]] static constexpr FrameId eclipj2000() noexcept { return FrameId(FrameKind::eclipj2000, {}); }

    /// The IAU body-fixed frame of `body`.
    [[nodiscard]] static constexpr FrameId body_fixed(catalog::BodyId body) noexcept {
        return FrameId(FrameKind::body_fixed, body);
    }

    /// The frame's kind.
    [[nodiscard]] constexpr FrameKind kind() const noexcept { return kind_; }

    /// The body of a body-fixed frame; an invalid BodyId for the other kinds.
    [[nodiscard]] constexpr catalog::BodyId body() const noexcept { return body_; }

    /// True for frames that do not rotate with respect to the ICRF.
    [[nodiscard]] constexpr bool is_inertial() const noexcept { return kind_ != FrameKind::body_fixed; }

    /// Same kind and, for body-fixed frames, the same body.
    friend constexpr bool operator==(const FrameId&, const FrameId&) noexcept = default;

private:
    constexpr FrameId(FrameKind kind, catalog::BodyId body) noexcept : kind_(kind), body_(body) {}

    FrameKind kind_;
    catalog::BodyId body_;
};

/// Name of the frame: "ICRF", "ECLIPJ2000" or "body-fixed(naif:399)".
[[nodiscard]] std::string to_string(FrameId frame);

} // namespace usim::frames
