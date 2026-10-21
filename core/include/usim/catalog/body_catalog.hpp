#pragma once

#include "usim/catalog/body.hpp"
#include "usim/catalog/body_id.hpp"
#include "usim/catalog/quantity.hpp"
#include "usim/result.hpp"

#include <cstddef>
#include <span>
#include <string_view>
#include <vector>

namespace usim::catalog {

/// An immutable, validated set of bodies and the sources their values cite. Bodies form a forest through
/// their primary links (SPEC section 7.5). Safe to share between threads.
class BodyCatalog {
public:
    /// An empty catalog.
    BodyCatalog() = default;

    /// Builds a catalog. Bodies keep the given order. Fails with ErrorCode::data_format if:
    /// - a source id is empty or repeated;
    /// - a body id is invalid or repeated, or a name is empty;
    /// - a primary is missing from the catalog, or the primary links form a cycle;
    /// - a value is non-finite, cites an unregistered source, or has an uncertainty that is negative,
    ///   non-finite or present without a value.
    [[nodiscard]] static Result<BodyCatalog> create(std::vector<Source> sources, std::vector<Body> bodies);

    /// All bodies, in catalog order.
    [[nodiscard]] std::span<const Body> bodies() const noexcept { return bodies_; }

    /// All sources, in catalog order.
    [[nodiscard]] std::span<const Source> sources() const noexcept { return sources_; }

    /// Number of bodies.
    [[nodiscard]] std::size_t size() const noexcept { return bodies_.size(); }

    /// The body with `id`, or nullptr. O(log n).
    [[nodiscard]] const Body* find(BodyId id) const noexcept;

    /// The source with registry id `id`, or nullptr.
    [[nodiscard]] const Source* find_source(std::string_view id) const noexcept;

    /// Bodies whose primary is `id`, in catalog order. Empty if there are none or `id` is unknown.
    [[nodiscard]] std::vector<BodyId> children(BodyId id) const;

    /// Bodies without a primary, in catalog order.
    [[nodiscard]] std::vector<BodyId> roots() const;

    /// The chain from `id` to its root: id, its primary, the primary's primary, ..., the root.
    /// Fails with ErrorCode::not_found if `id` is not in the catalog.
    [[nodiscard]] Result<std::vector<BodyId>> ancestry(BodyId id) const;

private:
    std::vector<Source> sources_;
    std::vector<Body> bodies_;
    std::vector<std::size_t> order_by_id_; // indices into bodies_, sorted by BodyId
};

} // namespace usim::catalog
