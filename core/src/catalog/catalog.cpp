#include "usim/catalog/body.hpp"
#include "usim/catalog/body_catalog.hpp"
#include "usim/catalog/body_id.hpp"
#include "usim/catalog/quantity.hpp"

#include <algorithm>
#include <cmath>
#include <string>
#include <utility>

namespace usim::catalog {

std::string_view to_string(IdNamespace id_namespace) noexcept {
    switch (id_namespace) {
    case IdNamespace::invalid:
        return "invalid";
    case IdNamespace::naif:
        return "naif";
    case IdNamespace::gaia_dr3:
        return "gaia-dr3";
    case IdNamespace::hipparcos:
        return "hip";
    case IdNamespace::hr:
        return "hr";
    }
    return "invalid";
}

std::string to_string(BodyId id) {
    return std::string(to_string(id.id_namespace)) + ":" + std::to_string(id.value);
}

std::string_view to_string(Unit unit) noexcept {
    switch (unit) {
    case Unit::none:
        return "";
    case Unit::km:
        return "km";
    case Unit::s:
        return "s";
    case Unit::km_per_s:
        return "km/s";
    case Unit::rad:
        return "rad";
    case Unit::rad_per_s:
        return "rad/s";
    case Unit::km3_per_s2:
        return "km^3/s^2";
    }
    return "";
}

std::string_view to_string(BodyKind kind) noexcept {
    switch (kind) {
    case BodyKind::barycenter:
        return "barycenter";
    case BodyKind::star:
        return "star";
    case BodyKind::planet:
        return "planet";
    case BodyKind::dwarf_planet:
        return "dwarf_planet";
    case BodyKind::moon:
        return "moon";
    case BodyKind::asteroid:
        return "asteroid";
    case BodyKind::comet:
        return "comet";
    case BodyKind::spacecraft:
        return "spacecraft";
    case BodyKind::star_system:
        return "star_system";
    case BodyKind::galaxy:
        return "galaxy";
    }
    return "unknown";
}

namespace {

Error catalog_error(std::string message) {
    return make_error(ErrorCode::data_format, "catalog: " + std::move(message));
}

// Checks one quantity. Returns an empty string if it is valid, else the problem. `source_ids` is sorted.
std::string check_quantity(const Quantity& q, std::string_view field, const std::vector<std::string_view>& source_ids) {
    const std::string where = std::string(field) + ": ";
    if (!q.value) {
        return q.uncertainty ? where + "uncertainty without a value" : std::string();
    }
    if (!std::isfinite(*q.value)) {
        return where + "value is not finite";
    }
    if (q.uncertainty && (!std::isfinite(*q.uncertainty) || *q.uncertainty < 0.0)) {
        return where + "uncertainty must be finite and non-negative";
    }
    if (!std::binary_search(source_ids.begin(), source_ids.end(), std::string_view(q.source.source_id))) {
        return where + "cites unregistered source \"" + q.source.source_id + "\"";
    }
    return {};
}

} // namespace

Result<BodyCatalog> BodyCatalog::create(std::vector<Source> sources, std::vector<Body> bodies) {
    BodyCatalog catalog;

    // Sources: non-empty, unique ids.
    std::vector<std::string_view> source_ids;
    source_ids.reserve(sources.size());
    for (const Source& source : sources) {
        source_ids.emplace_back(source.id);
    }
    std::sort(source_ids.begin(), source_ids.end());
    for (std::size_t i = 0; i < source_ids.size(); ++i) {
        if (source_ids[i].empty()) {
            return catalog_error("a source has an empty id");
        }
        if (i > 0 && source_ids[i] == source_ids[i - 1]) {
            return catalog_error("duplicate source id \"" + std::string(source_ids[i]) + "\"");
        }
    }

    // Bodies: valid, unique ids, names, quantities.
    catalog.order_by_id_.resize(bodies.size());
    for (std::size_t i = 0; i < bodies.size(); ++i) {
        catalog.order_by_id_[i] = i;
    }
    std::sort(catalog.order_by_id_.begin(), catalog.order_by_id_.end(),
              [&](std::size_t a, std::size_t b) { return bodies[a].id < bodies[b].id; });
    for (std::size_t k = 0; k < catalog.order_by_id_.size(); ++k) {
        const Body& body = bodies[catalog.order_by_id_[k]];
        if (!body.id.is_valid()) {
            return catalog_error("a body has an invalid id");
        }
        if (k > 0 && body.id == bodies[catalog.order_by_id_[k - 1]].id) {
            return catalog_error("duplicate body id " + to_string(body.id));
        }
    }
    for (const Body& body : bodies) {
        const std::string label = to_string(body.id);
        if (body.name.empty()) {
            return catalog_error(label + " has an empty name");
        }
        const std::pair<const Quantity*, std::string_view> fields[] = {{&body.gm, "gm"},
                                                                        {&body.mean_radius, "mean_radius"},
                                                                        {&body.radius_a, "radius_a"},
                                                                        {&body.radius_b, "radius_b"},
                                                                        {&body.radius_c, "radius_c"}};
        for (const auto& [quantity, field] : fields) {
            const std::string problem = check_quantity(*quantity, field, source_ids);
            if (!problem.empty()) {
                return catalog_error(label + " " + problem);
            }
        }
    }

    // source_ids views the strings in `sources`; it is not used after this move.
    catalog.sources_ = std::move(sources);
    catalog.bodies_ = std::move(bodies);

    // Hierarchy: primaries exist and do not form cycles.
    for (const Body& body : catalog.bodies_) {
        if (!body.primary) {
            continue;
        }
        if (catalog.find(*body.primary) == nullptr) {
            return catalog_error(to_string(body.id) + " has unknown primary " + to_string(*body.primary));
        }
        // Walking up more steps than there are bodies means the links loop.
        const Body* current = &body;
        std::size_t steps = 0;
        while (current->primary) {
            if (++steps > catalog.bodies_.size()) {
                return catalog_error("the primary links of " + to_string(body.id) + " form a cycle");
            }
            current = catalog.find(*current->primary);
        }
    }
    return catalog;
}

const Body* BodyCatalog::find(BodyId id) const noexcept {
    const auto it = std::lower_bound(order_by_id_.begin(), order_by_id_.end(), id,
                                     [&](std::size_t index, BodyId key) { return bodies_[index].id < key; });
    if (it == order_by_id_.end() || bodies_[*it].id != id) {
        return nullptr;
    }
    return &bodies_[*it];
}

const Source* BodyCatalog::find_source(std::string_view id) const noexcept {
    const auto it = std::find_if(sources_.begin(), sources_.end(), [&](const Source& s) { return s.id == id; });
    return it == sources_.end() ? nullptr : &*it;
}

std::vector<BodyId> BodyCatalog::children(BodyId id) const {
    std::vector<BodyId> result;
    for (const Body& body : bodies_) {
        if (body.primary && *body.primary == id) {
            result.push_back(body.id);
        }
    }
    return result;
}

std::vector<BodyId> BodyCatalog::roots() const {
    std::vector<BodyId> result;
    for (const Body& body : bodies_) {
        if (!body.primary) {
            result.push_back(body.id);
        }
    }
    return result;
}

Result<std::vector<BodyId>> BodyCatalog::ancestry(BodyId id) const {
    const Body* current = find(id);
    if (current == nullptr) {
        return make_error(ErrorCode::not_found, "catalog: unknown body " + to_string(id));
    }
    std::vector<BodyId> chain{current->id};
    while (current->primary) {
        current = find(*current->primary); // validated in create(): never null, never cyclic
        chain.push_back(current->id);
    }
    return chain;
}

} // namespace usim::catalog
