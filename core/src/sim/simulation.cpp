#include "usim/sim/simulation.hpp"

#include <algorithm>
#include <string>
#include <utility>

namespace usim::sim {

Simulation::Simulation(std::unique_ptr<const data::DataPack> pack, ephemeris::EphemerisChain chain,
                       frames::FrameSystem frames, ephemeris::TimeInterval coverage,
                       std::vector<SnapshotBody> bodies)
    : pack_(std::move(pack)), chain_(std::move(chain)), frames_(std::move(frames)), coverage_(coverage),
      clock_(coverage, time::Instant()), bodies_(std::move(bodies)) {
    for (const catalog::Body& body : pack_->catalog().bodies()) {
        if (body.kind == catalog::BodyKind::star && chain_.contains(body.id)) {
            light_ = body.id;
            break;
        }
    }
}

Result<Simulation> Simulation::create(data::DataPack pack) {
    auto owned = std::make_unique<const data::DataPack>(std::move(pack));
    Result<ephemeris::EphemerisChain> chain = ephemeris::EphemerisChain::create(*owned);
    if (!chain) {
        return chain.error();
    }
    frames::FrameSystem frames(std::vector<frames::IauRotationElements>(owned->rotation_elements().begin(),
                                                                        owned->rotation_elements().end()));

    // The clock's limits: where every catalog body with a position is covered. With gaps, the span of
    // that intersection; queries in a gap still report out of coverage.
    std::vector<SnapshotBody> bodies;
    std::optional<ephemeris::Coverage> common;
    for (const catalog::Body& body : owned->catalog().bodies()) {
        SnapshotBody entry;
        entry.id = body.id;
        entry.has_radius = body.mean_radius.has_value();
        entry.radius_km = body.mean_radius.value.value_or(0.0);
        if (body.radius_a.has_value() && body.radius_b.has_value() && body.radius_c.has_value()) {
            entry.radii_km = {*body.radius_a.value, *body.radius_b.value, *body.radius_c.value};
        } else {
            entry.radii_km = {entry.radius_km, entry.radius_km, entry.radius_km};
        }
        entry.max_radius_km =
            std::max({entry.radius_km, entry.radii_km.x, entry.radii_km.y, entry.radii_km.z});
        entry.has_rotation = frames.find_rotation(body.id) != nullptr;
        const auto is_planet = [](catalog::BodyKind k) {
            return k == catalog::BodyKind::planet || k == catalog::BodyKind::dwarf_planet;
        };
        entry.enlarge = is_planet(body.kind) || body.kind == catalog::BodyKind::moon;
        if (entry.enlarge && body.primary) {
            // Partners share the primary (the system barycenter): a planet and its moons.
            const auto all = owned->catalog().bodies();
            for (std::size_t j = 0; j < all.size(); ++j) {
                const catalog::Body& other = all[j];
                const bool partner =
                    other.id != body.id && other.primary == body.primary && other.mean_radius.has_value() &&
                    (body.kind == catalog::BodyKind::moon ? is_planet(other.kind)
                                                          : other.kind == catalog::BodyKind::moon);
                if (partner) {
                    entry.partners.push_back(static_cast<std::uint32_t>(j));
                }
            }
        }
        bodies.push_back(std::move(entry));
        if (chain.value().contains(body.id)) {
            const ephemeris::Coverage covered = chain.value().coverage(body.id).value();
            common = common ? common->intersection(covered) : covered;
        }
    }
    const std::optional<ephemeris::TimeInterval> span = common ? common->span() : std::nullopt;
    if (!span) {
        return make_error(ErrorCode::data_format,
                          "the pack's ephemeris covers no time span common to all its bodies");
    }
    return Simulation(std::move(owned), std::move(chain).value(), std::move(frames), *span,
                      std::move(bodies));
}

Result<Simulation> Simulation::load(std::span<const std::byte> bytes) {
    Result<data::DataPack> pack = data::DataPack::parse(bytes);
    if (!pack) {
        return pack.error();
    }
    return create(std::move(pack).value());
}

Result<Simulation> Simulation::load_file(const std::filesystem::path& path) {
    Result<data::DataPack> pack = data::DataPack::load_file(path);
    if (!pack) {
        return pack.error();
    }
    return create(std::move(pack).value());
}

Result<BodyState> Simulation::state(catalog::BodyId body, catalog::BodyId center, frames::FrameId frame,
                                    time::Instant t) const {
    // Inertial frames come straight from the chain; a body-fixed frame is applied to the ICRF state.
    const frames::FrameId chain_frame = frame.is_inertial() ? frame : frames::FrameId::icrf();
    Result<frames::StateVector> state = chain_.state(body, center, chain_frame, t);
    if (!state) {
        return state.error();
    }
    BodyState result{body, center,        frame,
                     t,    state.value(), {StateModel::ephemeris, chain_.is_approximate(body, center)}};
    if (!frame.is_inertial()) {
        Result<frames::FrameTransform> to_frame = frames_.transform(frames::FrameId::icrf(), frame, t);
        if (!to_frame) {
            return to_frame.error();
        }
        result.state = to_frame.value().apply(state.value());
    }
    return result;
}

Result<math::Quat> Simulation::orientation(catalog::BodyId body, frames::FrameId frame,
                                           time::Instant t) const {
    if (!t.is_valid()) {
        return make_error(ErrorCode::invalid_argument, "invalid instant");
    }
    Result<frames::FrameTransform> to_frame = frames_.transform(frames::FrameId::body_fixed(body), frame, t);
    if (!to_frame) {
        return to_frame.error();
    }
    return math::Quat::from_matrix(to_frame.value().rotation());
}

Result<catalog::BodyId> Simulation::default_orbit_center(catalog::BodyId body) const {
    const catalog::BodyCatalog& bodies = pack_->catalog();
    const catalog::Body* entry = bodies.find(body);
    if (entry == nullptr) {
        return make_error(ErrorCode::not_found, "no body " + catalog::to_string(body) + " in the catalog");
    }
    const auto is_planet = [](catalog::BodyKind kind) {
        return kind == catalog::BodyKind::planet || kind == catalog::BodyKind::dwarf_planet;
    };
    if (entry->kind == catalog::BodyKind::moon && entry->primary) {
        for (const catalog::BodyId sibling : bodies.children(*entry->primary)) {
            if (is_planet(bodies.find(sibling)->kind)) {
                return sibling;
            }
        }
    }
    const Result<std::vector<catalog::BodyId>> ancestry = bodies.ancestry(body);
    const catalog::BodyId root = ancestry.value().back();
    if (body == root) {
        return make_error(ErrorCode::not_found, "the root " + catalog::to_string(body) + " has no orbit");
    }
    if (entry->kind == catalog::BodyKind::star) {
        return root;
    }
    for (const catalog::BodyId child : bodies.children(root)) {
        if (bodies.find(child)->kind == catalog::BodyKind::star) {
            return child;
        }
    }
    return make_error(ErrorCode::not_found,
                      "the catalog has no star to draw the orbit of " + catalog::to_string(body) + " around");
}

Result<std::size_t> Simulation::fill_stars(const SnapshotRequest& request, std::span<StarSnapshot> out,
                                           std::size_t first) const {
    if (!request.frame.is_inertial()) {
        return make_error(ErrorCode::invalid_argument, "the stars' display frame must be the ICRF or ECLIPJ2000");
    }
    const std::vector<catalog::Star>& stars = pack_->star_catalog().stars;
    if (first > stars.size()) {
        return make_error(ErrorCode::invalid_argument, "the first star is past the end of the catalog");
    }
    const time::Instant t = clock_.now();
    const std::optional<frames::FrameTransform> to_display =
        frames_.try_transform(frames::FrameId::icrf(), request.frame, t);
    if (!to_display) {
        return make_error(ErrorCode::internal, "no rotation from the ICRF to the display frame");
    }
    const math::Mat3 rotation = to_display->rotation();
    const double t_s = t.seconds_since_j2000();
    const double epoch_s = pack_->star_catalog().epoch_tdb_s;
    const std::size_t count = std::min(out.size(), stars.size() - first);
    for (std::size_t i = 0; i < count; ++i) {
        const catalog::Star& star = stars[first + i];
        StarSnapshot& s = out[i];
        s.direction = presentation::to_host(request.view.axes, rotation * catalog::star_direction_icrf(star, epoch_s, t_s));
        s.v_magnitude = star.v_magnitude;
        s.b_minus_v = star.b_minus_v.value_or(0.0);
        s.flags = star.b_minus_v ? star_has_color : 0u;
    }
    return count;
}

Result<std::size_t> Simulation::fill_snapshot(const SnapshotRequest& request, std::span<BodySnapshot> out,
                                              std::size_t first) const {
    if (Result<void> valid = presentation::validate(request.view); !valid) {
        return valid.error();
    }
    if (!request.frame.is_inertial()) {
        return make_error(ErrorCode::invalid_argument,
                          "a snapshot's display frame must be the ICRF or ECLIPJ2000");
    }
    if (first > bodies_.size()) {
        return make_error(ErrorCode::invalid_argument,
                          "the first snapshot body is past the end of the catalog");
    }
    if (!chain_.contains(request.center)) {
        return make_error(ErrorCode::not_found,
                          "no ephemeris for the snapshot center " + catalog::to_string(request.center));
    }
    const time::Instant t = clock_.now();
    const presentation::View& view = request.view;
    const std::size_t count = std::min(out.size(), bodies_.size() - first);
    for (std::size_t i = 0; i < count; ++i) {
        const SnapshotBody& body = bodies_[first + i];
        BodySnapshot& s = out[i];
        s = BodySnapshot{};
        s.body = body.id;

        double compression = 1.0;
        const ephemeris::Evaluation e = chain_.evaluate(body.id, request.center, request.frame, t);
        if (e.ok()) {
            const math::Vec3 position = presentation::to_render_position(view, e.state.position_km);
            compression = presentation::distance_compression(view, math::norm(position));
            s.render_position = position * compression;
            if (compression < 1.0) {
                s.flags |= snapshot_compressed;
            }
            if (e.approximate) {
                s.flags |= snapshot_approximate;
            }
        } else {
            s.flags |= snapshot_no_position;
        }

        const ephemeris::Evaluation to_light =
            light_.is_valid() && body.id != light_
                ? chain_.evaluate(light_, body.id, request.frame, t)
                : ephemeris::Evaluation{{}, ErrorCode::not_found, {}, false};
        const double light_distance = to_light.ok() ? math::norm(to_light.state.position_km) : 0.0;
        if (light_distance > 0.0) {
            s.light_direction = presentation::to_host(view.axes, to_light.state.position_km / light_distance);
        } else {
            s.flags |= snapshot_no_light;
        }

        std::optional<frames::FrameTransform> to_display;
        if (body.has_rotation) {
            to_display = frames_.try_transform(frames::FrameId::body_fixed(body.id), request.frame, t);
        }
        if (to_display) {
            s.orientation =
                presentation::to_render_rotation(view, math::Quat::from_matrix(to_display->rotation()));
        } else {
            s.flags |= snapshot_no_orientation;
        }

        if (body.has_radius) {
            double scale = view.scale_mode == presentation::ScaleMode::enlarged_bodies && body.enlarge
                               ? view.body_scale
                               : 1.0;
            for (const std::uint32_t j : body.partners) {
                if (!(scale > 1.0)) {
                    break;
                }
                const ephemeris::Evaluation apart = chain_.evaluate(bodies_[j].id, body.id, request.frame, t);
                if (apart.ok()) {
                    const double limit = 0.4 * math::norm(apart.state.position_km) / body.max_radius_km;
                    scale = std::max(1.0, std::min(scale, limit));
                }
            }
            presentation::View true_scale = view;
            true_scale.scale_mode = presentation::ScaleMode::true_scale;
            s.render_radius =
                presentation::to_render_radius(true_scale, body.radius_km) * scale * compression;
            s.render_radii = presentation::to_render_radii(true_scale, body.radii_km) * (scale * compression);
        } else {
            s.flags |= snapshot_no_radius;
        }
    }
    return count;
}

} // namespace usim::sim
