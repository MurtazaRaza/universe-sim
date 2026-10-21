#pragma once

#include <compare>
#include <cstdint>
#include <string>
#include <string_view>

namespace usim::catalog {

/// The identifier scheme a BodyId value belongs to (SPEC section 7.4). Values are stable and shared with
/// the C ABI (`id_namespace`); never renumber them, only append.
enum class IdNamespace : std::int32_t {
    invalid = 0,   ///< Not a valid namespace; marks an unset identifier.
    naif = 1,      ///< NAIF integer IDs (0 SSB, 10 Sun, 399 Earth, 301 Moon, 2000001 Ceres, ...).
    gaia_dr3 = 2,  ///< Gaia DR3 source_id (Phase 9).
    hipparcos = 3, ///< Hipparcos catalog number (Phase 9).
};

/// Short lowercase name of the namespace: "naif", "gaia-dr3", "hip" or "invalid".
[[nodiscard]] std::string_view to_string(IdNamespace id_namespace) noexcept;

/// Identifies a body: a namespace and a value within it (SPEC section 7.4).
struct BodyId {
    IdNamespace id_namespace = IdNamespace::invalid; ///< Identifier scheme.
    std::int64_t value = 0;                          ///< Identifier within the scheme.

    /// The body with NAIF ID `naif_id`.
    [[nodiscard]] static constexpr BodyId naif(std::int64_t naif_id) noexcept {
        return {IdNamespace::naif, naif_id};
    }

    /// True unless the namespace is IdNamespace::invalid.
    [[nodiscard]] constexpr bool is_valid() const noexcept { return id_namespace != IdNamespace::invalid; }

    /// Orders by namespace, then value. Gives catalogs a deterministic order.
    friend constexpr auto operator<=>(const BodyId&, const BodyId&) noexcept = default;
};

/// Text form "namespace:value", for example "naif:399".
[[nodiscard]] std::string to_string(BodyId id);

} // namespace usim::catalog
