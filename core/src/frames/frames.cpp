#include "usim/frames/frame_id.hpp"
#include "usim/frames/frame_system.hpp"

#include "usim/constants.hpp"

#include <algorithm>
#include <utility>

namespace usim::frames {

std::string to_string(FrameId frame) {
    switch (frame.kind()) {
    case FrameKind::icrf:
        return "ICRF";
    case FrameKind::eclipj2000:
        return "ECLIPJ2000";
    case FrameKind::body_fixed:
        return "body-fixed(" + catalog::to_string(frame.body()) + ")";
    }
    return "unknown";
}

math::Mat3 icrf_to_eclipj2000() noexcept {
    // Rotating the frame by +obliquity about X equals rotating vectors by -obliquity.
    return math::Mat3::rotation_x(-constants::k_eclipj2000_obliquity_rad);
}

FrameSystem::FrameSystem(std::vector<IauRotationElements> rotations) : rotations_(std::move(rotations)) {
    // Stable, so the first elements of a body come first, and the duplicates after them are dropped.
    std::stable_sort(rotations_.begin(), rotations_.end(),
                     [](const IauRotationElements& a, const IauRotationElements& b) { return a.body < b.body; });
    rotations_.erase(std::unique(rotations_.begin(), rotations_.end(),
                                 [](const IauRotationElements& a, const IauRotationElements& b) { return a.body == b.body; }),
                     rotations_.end());
}

const IauRotationElements* FrameSystem::find_rotation(catalog::BodyId body) const noexcept {
    const auto it = std::lower_bound(rotations_.begin(), rotations_.end(), body,
                                     [](const IauRotationElements& e, catalog::BodyId id) { return e.body < id; });
    return it != rotations_.end() && it->body == body ? &*it : nullptr;
}

std::optional<FrameTransform> FrameSystem::try_transform(FrameId from, FrameId to, time::Instant t) const noexcept {
    if (from == to) {
        return FrameTransform{};
    }
    // Every frame is defined relative to the ICRF, so route through it.
    const std::optional<FrameTransform> from_to_icrf = from_icrf(from, t);
    const std::optional<FrameTransform> icrf_to_target = from_icrf(to, t);
    if (!from_to_icrf || !icrf_to_target) {
        return std::nullopt;
    }
    return from_to_icrf->inverse().then(*icrf_to_target);
}

Result<FrameTransform> FrameSystem::transform(FrameId from, FrameId to, time::Instant t) const {
    if (std::optional<FrameTransform> result = try_transform(from, to, t)) {
        return *result;
    }
    const FrameId missing = from_icrf(from, t) ? to : from;
    return make_error(ErrorCode::not_found, "no rotation model for the frame " + to_string(missing));
}

std::optional<FrameTransform> FrameSystem::from_icrf(FrameId frame, time::Instant t) const noexcept {
    switch (frame.kind()) {
    case FrameKind::icrf:
        return FrameTransform{};
    case FrameKind::eclipj2000:
        return FrameTransform::inertial(icrf_to_eclipj2000());
    case FrameKind::body_fixed:
        if (const IauRotationElements* elements = find_rotation(frame.body())) {
            return iau_transform(*elements, t);
        }
        return std::nullopt;
    }
    return std::nullopt;
}

} // namespace usim::frames
