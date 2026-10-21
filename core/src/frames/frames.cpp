#include "usim/frames/frame_id.hpp"
#include "usim/frames/frame_system.hpp"

#include "usim/constants.hpp"

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

Result<FrameTransform> FrameSystem::transform(FrameId from, FrameId to, time::Instant t) const {
    if (from == to) {
        return FrameTransform{};
    }
    // Every frame is defined relative to the ICRF, so route through it.
    const Result<FrameTransform> from_to_icrf = from_icrf(from, t);
    if (!from_to_icrf) {
        return from_to_icrf.error();
    }
    const Result<FrameTransform> icrf_to_target = from_icrf(to, t);
    if (!icrf_to_target) {
        return icrf_to_target.error();
    }
    return from_to_icrf.value().inverse().then(icrf_to_target.value());
}

Result<FrameTransform> FrameSystem::from_icrf(FrameId frame, time::Instant /*t*/) const {
    switch (frame.kind()) {
    case FrameKind::icrf:
        return FrameTransform{};
    case FrameKind::eclipj2000:
        return FrameTransform::inertial(icrf_to_eclipj2000());
    case FrameKind::body_fixed:
        return make_error(ErrorCode::unsupported,
                          "body-fixed frames are not available yet (planned in P4-03): " + to_string(frame));
    }
    return make_error(ErrorCode::invalid_argument, "unknown frame kind");
}

} // namespace usim::frames
