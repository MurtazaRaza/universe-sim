#include "usim/ephemeris/ephemeris_chain.hpp"

#include "usim/frames/frame_system.hpp"

#include <algorithm>
#include <cstdio>
#include <string>
#include <utility>

namespace usim::ephemeris {

namespace {

// Seconds for a message, to 17 significant digits ("6311390400", "-1.5"). snprintf rather than
// std::to_chars, whose floating-point form needs macOS 13.3 while the plugin supports macOS 12.
std::string seconds_text(double seconds) {
    char buffer[32];
    std::snprintf(buffer, sizeof buffer, "%.17g", seconds);
    return buffer;
}

Error unknown_body(catalog::BodyId body) {
    return make_error(ErrorCode::not_found, "no ephemeris data for " + catalog::to_string(body));
}

Error not_connected(catalog::BodyId target, catalog::BodyId center) {
    return make_error(ErrorCode::not_found, "no chain of ephemeris segments connects " +
                                                catalog::to_string(target) + " and " +
                                                catalog::to_string(center));
}

} // namespace

Result<EphemerisChain> EphemerisChain::create(std::span<const data::EphemerisSegment> segments,
                                              std::span<const data::PositionApproximation> approximations) {
    EphemerisChain chain;
    chain.icrf_to_ecliptic_ = frames::icrf_to_eclipj2000();
    chain.ecliptic_to_icrf_ = math::transpose(chain.icrf_to_ecliptic_);

    std::vector<TimeInterval> spans;
    for (const data::EphemerisSegment& segment : segments) {
        if (segment.target == segment.center) {
            return make_error(ErrorCode::data_format,
                              "segment of " + catalog::to_string(segment.target) + " is relative to itself");
        }
        Result<ChebyshevSegment> evaluator = ChebyshevSegment::create(segment);
        if (!evaluator) {
            return evaluator.error();
        }
        spans.push_back(evaluator.value().coverage());
        chain.segments_.push_back(evaluator.value());
        chain.ids_.push_back(segment.target);
        chain.ids_.push_back(segment.center);
    }
    if (chain.segments_.size() >= k_no_parent) {
        return make_error(ErrorCode::data_format, "too many ephemeris segments");
    }
    const std::vector<catalog::BodyId> reachable = [&] {
        std::vector<catalog::BodyId> ids = chain.ids_;
        std::sort(ids.begin(), ids.end());
        return ids;
    }();
    for (const data::PositionApproximation& a : approximations) {
        const std::string what = "approximate position of " + catalog::to_string(a.body);
        if (std::any_of(segments.begin(), segments.end(),
                        [&](const auto& s) { return s.target == a.body; })) {
            return make_error(ErrorCode::data_format, what + ": the body has its own ephemeris segment");
        }
        if (!std::binary_search(reachable.begin(), reachable.end(), a.proxy)) {
            return make_error(ErrorCode::data_format,
                              what + ": its proxy " + catalog::to_string(a.proxy) + " has no ephemeris");
        }
        if (std::count_if(approximations.begin(), approximations.end(),
                          [&](const auto& b) { return b.body == a.body; }) > 1) {
            return make_error(ErrorCode::data_format, what + " appears twice");
        }
        chain.ids_.push_back(a.body);
    }
    std::sort(chain.ids_.begin(), chain.ids_.end());
    chain.ids_.erase(std::unique(chain.ids_.begin(), chain.ids_.end()), chain.ids_.end());
    chain.nodes_.resize(chain.ids_.size());
    chain.all_ = Coverage::of(spans);
    if (const auto all = chain.all_.span()) {
        chain.all_ = Coverage::of(std::span<const TimeInterval>(&*all, 1));
    }

    // Segments of each target, in reverse pack order (highest precedence first), and the parent links.
    std::vector<std::vector<std::uint32_t>> by_node(chain.nodes_.size());
    for (std::size_t i = chain.segments_.size(); i-- > 0;) {
        const ChebyshevSegment& segment = chain.segments_[i];
        const std::uint32_t n = chain.find(segment.target());
        const std::uint32_t center = chain.find(segment.center());
        Node& node = chain.nodes_[n];
        if (node.parent != k_no_parent && node.parent != center) {
            return make_error(ErrorCode::unsupported,
                              "the segments of " + catalog::to_string(segment.target()) +
                                  " have different centers (" + catalog::to_string(chain.ids_[node.parent]) +
                                  " and " + catalog::to_string(segment.center()) + ")");
        }
        node.parent = center;
        by_node[n].push_back(static_cast<std::uint32_t>(i));
    }
    for (std::size_t n = 0; n < chain.nodes_.size(); ++n) {
        Node& node = chain.nodes_[n];
        node.first_segment = static_cast<std::uint32_t>(chain.order_.size());
        node.segment_count = static_cast<std::uint32_t>(by_node[n].size());
        std::vector<TimeInterval> own;
        for (const std::uint32_t s : by_node[n]) {
            chain.order_.push_back(s);
            own.push_back(chain.segments_[s].coverage());
        }
        node.own_coverage = Coverage::of(own);
    }
    for (const data::PositionApproximation& a : approximations) {
        Node& node = chain.nodes_[chain.find(a.body)];
        node.parent = chain.find(a.proxy);
        node.approximation = &a;
        node.own_coverage = chain.all_; // the zero offset holds whenever the proxy is covered
    }

    // Depths and root coverages, walking each path to its root. A path longer than the number of nodes
    // has a cycle.
    for (std::size_t n = 0; n < chain.nodes_.size(); ++n) {
        std::uint32_t depth = 0;
        Coverage covered = chain.all_;
        for (std::uint32_t m = static_cast<std::uint32_t>(n); chain.nodes_[m].parent != k_no_parent;
             m = chain.nodes_[m].parent) {
            if (++depth > chain.nodes_.size()) {
                return make_error(ErrorCode::data_format, "ephemeris segments form a cycle through " +
                                                              catalog::to_string(chain.ids_[n]));
            }
            covered = covered.intersection(chain.nodes_[m].own_coverage);
        }
        chain.nodes_[n].depth = depth;
        chain.nodes_[n].root_coverage = std::move(covered);
    }
    return chain;
}

std::uint32_t EphemerisChain::find(catalog::BodyId body) const noexcept {
    const auto it = std::lower_bound(ids_.begin(), ids_.end(), body);
    return it != ids_.end() && *it == body ? static_cast<std::uint32_t>(it - ids_.begin()) : k_no_parent;
}

bool EphemerisChain::contains(catalog::BodyId body) const noexcept {
    return find(body) != k_no_parent;
}

std::optional<catalog::BodyId> EphemerisChain::parent(catalog::BodyId body) const noexcept {
    const std::uint32_t n = find(body);
    if (n == k_no_parent || nodes_[n].parent == k_no_parent) {
        return std::nullopt;
    }
    return ids_[nodes_[n].parent];
}

Result<std::vector<catalog::BodyId>> EphemerisChain::path(catalog::BodyId body) const {
    std::uint32_t n = find(body);
    if (n == k_no_parent) {
        return unknown_body(body);
    }
    std::vector<catalog::BodyId> result{ids_[n]};
    for (; nodes_[n].parent != k_no_parent; n = nodes_[n].parent) {
        result.push_back(ids_[nodes_[n].parent]);
    }
    return result;
}

std::vector<const ChebyshevSegment*> EphemerisChain::segments_of(catalog::BodyId body) const {
    std::vector<const ChebyshevSegment*> result;
    const std::uint32_t n = find(body);
    if (n != k_no_parent) {
        for (std::uint32_t i = 0; i < nodes_[n].segment_count; ++i) {
            result.push_back(&segments_[order_[nodes_[n].first_segment + i]]);
        }
    }
    return result;
}

const data::PositionApproximation* EphemerisChain::approximation(catalog::BodyId body) const noexcept {
    const std::uint32_t n = find(body);
    return n == k_no_parent ? nullptr : nodes_[n].approximation;
}

bool EphemerisChain::is_approximate(catalog::BodyId target, catalog::BodyId center) const noexcept {
    std::uint32_t a = find(target);
    std::uint32_t b = find(center);
    if (a == k_no_parent || b == k_no_parent) {
        return false;
    }
    bool approximate = false;
    while (a != b) {
        const std::uint32_t depth_a = nodes_[a].depth;
        const std::uint32_t depth_b = nodes_[b].depth;
        if (depth_a == 0 && depth_b == 0) {
            return false; // not connected
        }
        if (depth_a >= depth_b) {
            approximate = approximate || nodes_[a].approximation != nullptr;
            a = nodes_[a].parent;
        }
        if (depth_b >= depth_a) {
            approximate = approximate || nodes_[b].approximation != nullptr;
            b = nodes_[b].parent;
        }
    }
    return approximate;
}

Result<Coverage> EphemerisChain::coverage(catalog::BodyId body) const {
    const std::uint32_t n = find(body);
    if (n == k_no_parent) {
        return unknown_body(body);
    }
    return nodes_[n].root_coverage;
}

Result<Coverage> EphemerisChain::coverage(catalog::BodyId target, catalog::BodyId center) const {
    std::uint32_t a = find(target);
    std::uint32_t b = find(center);
    if (a == k_no_parent || b == k_no_parent) {
        return unknown_body(a == k_no_parent ? target : center);
    }
    Coverage covered = all_;
    while (a != b) {
        // Step up from the deeper node; at equal depth step up from both.
        const std::uint32_t depth_a = nodes_[a].depth;
        const std::uint32_t depth_b = nodes_[b].depth;
        if (depth_a == 0 && depth_b == 0) {
            return not_connected(target, center);
        }
        if (depth_a >= depth_b) {
            covered = covered.intersection(nodes_[a].own_coverage);
            a = nodes_[a].parent;
        }
        if (depth_b >= depth_a) {
            covered = covered.intersection(nodes_[b].own_coverage);
            b = nodes_[b].parent;
        }
    }
    return covered;
}

std::optional<frames::StateVector> EphemerisChain::step(std::uint32_t n, time::Instant t) const noexcept {
    const Node& node = nodes_[n];
    if (node.approximation != nullptr) {
        // Placed at its parent, within the span of the data, like the coverage this node reports.
        return node.own_coverage.contains(t) ? std::optional<frames::StateVector>(frames::StateVector{})
                                             : std::nullopt;
    }
    for (std::uint32_t i = 0; i < node.segment_count; ++i) {
        const ChebyshevSegment& segment = segments_[order_[node.first_segment + i]];
        if (const std::optional<frames::StateVector> state = segment.state(t)) {
            if (segment.frame().kind() == frames::FrameKind::eclipj2000) {
                return frames::StateVector{ecliptic_to_icrf_ * state->position_km,
                                           ecliptic_to_icrf_ * state->velocity_km_s};
            }
            return state;
        }
    }
    return std::nullopt;
}

Evaluation EphemerisChain::evaluate(catalog::BodyId target, catalog::BodyId center, frames::FrameId frame,
                                    time::Instant t) const noexcept {
    Evaluation result;
    if (frame.kind() != frames::FrameKind::icrf && frame.kind() != frames::FrameKind::eclipj2000) {
        result.error = ErrorCode::unsupported;
        return result;
    }
    std::uint32_t a = find(target);
    std::uint32_t b = find(center);
    if (a == k_no_parent || b == k_no_parent) {
        result.error = ErrorCode::not_found;
        result.failed_body = a == k_no_parent ? target : center;
        return result;
    }

    // Sum the states on both paths up to the common ancestor, in the ICRF.
    frames::StateVector up_a{}; // target relative to the ancestor
    frames::StateVector up_b{}; // center relative to the ancestor
    const auto climb = [&](std::uint32_t& n, frames::StateVector& sum) noexcept {
        const std::optional<frames::StateVector> s = step(n, t);
        if (!s) {
            result.error = ErrorCode::out_of_coverage;
            result.failed_body = ids_[n];
            return false;
        }
        sum.position_km += s->position_km;
        sum.velocity_km_s += s->velocity_km_s;
        result.approximate = result.approximate || nodes_[n].approximation != nullptr;
        n = nodes_[n].parent;
        return true;
    };
    while (a != b) {
        // Step up from the deeper node; at equal depth step up from both.
        const std::uint32_t depth_a = nodes_[a].depth;
        const std::uint32_t depth_b = nodes_[b].depth;
        if (depth_a == 0 && depth_b == 0) {
            result.error = ErrorCode::not_found; // two roots: not connected
            return result;
        }
        if (depth_a >= depth_b && !climb(a, up_a)) {
            return result;
        }
        if (depth_b >= depth_a && !climb(b, up_b)) {
            return result;
        }
    }
    if (!t.is_valid()) {
        result.error = ErrorCode::out_of_coverage; // target == center skips the segments, which check t
        return result;
    }

    result.state = {up_a.position_km - up_b.position_km, up_a.velocity_km_s - up_b.velocity_km_s};
    if (frame.kind() == frames::FrameKind::eclipj2000) {
        result.state = {icrf_to_ecliptic_ * result.state.position_km,
                        icrf_to_ecliptic_ * result.state.velocity_km_s};
    }
    return result;
}

Result<frames::StateVector> EphemerisChain::state(catalog::BodyId target, catalog::BodyId center,
                                                  frames::FrameId frame, time::Instant t) const {
    if (!t.is_valid()) {
        return make_error(ErrorCode::invalid_argument, "invalid instant");
    }
    const Evaluation e = evaluate(target, center, frame, t);
    if (e.ok()) {
        return e.state;
    }
    switch (*e.error) {
    case ErrorCode::unsupported:
        return make_error(ErrorCode::unsupported, "states in " + frames::to_string(frame) +
                                                      " are not available yet (planned in P4-03)");
    case ErrorCode::out_of_coverage: {
        std::string message = "no ephemeris segment of " + catalog::to_string(e.failed_body) +
                              " covers TDB " + seconds_text(t.seconds_since_j2000()) + " s past J2000";
        const std::uint32_t n = find(e.failed_body);
        if (const auto span = nodes_[n].own_coverage.span()) {
            message += " (coverage " + seconds_text(span->start.seconds_since_j2000()) + " to " +
                       seconds_text(span->end.seconds_since_j2000()) + " s)";
        }
        return make_error(ErrorCode::out_of_coverage, std::move(message));
    }
    case ErrorCode::not_found:
        return e.failed_body.is_valid() ? unknown_body(e.failed_body) : not_connected(target, center);
    default:
        return make_error(ErrorCode::internal, "unexpected ephemeris evaluation error");
    }
}

} // namespace usim::ephemeris
