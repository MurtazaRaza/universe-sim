#pragma once

#include "usim/catalog/body_id.hpp"
#include "usim/data/data_pack.hpp"
#include "usim/ephemeris/chebyshev_segment.hpp"
#include "usim/ephemeris/coverage.hpp"
#include "usim/frames/frame_id.hpp"
#include "usim/frames/frame_transform.hpp"
#include "usim/math/mat3.hpp"
#include "usim/result.hpp"
#include "usim/time/time_point.hpp"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>
#include <vector>

namespace usim::ephemeris {

/// The outcome of EphemerisChain::evaluate: a state, or the reason there is none. Never allocates.
struct Evaluation {
    /// Target relative to center in the requested frame: km and km/s. Zero when error is set.
    frames::StateVector state{};

    /// Empty on success. Otherwise ErrorCode::not_found (a body is not in the graph, or the two bodies are
    /// not connected), ErrorCode::out_of_coverage (no segment covers the instant) or ErrorCode::unsupported
    /// (the frame is not available yet).
    std::optional<ErrorCode> error;

    /// The body that caused the error: the unknown body, or the body whose segments do not cover the
    /// instant. Invalid on success, for frame errors and when the two bodies are not connected.
    catalog::BodyId failed_body;

    /// True if the path between the two bodies goes through a body placed at its primary (a
    /// data::PositionApproximation), so the state is approximate (SPEC FR-SIM-03, FR-SIM-04).
    bool approximate = false;

    /// True on success.
    [[nodiscard]] bool ok() const noexcept { return !error.has_value(); }
};

/// Resolves the state of any body relative to any other through the graph of ephemeris segments (SPEC
/// FR-SIM-01, ARCHITECTURE section 3.6).
///
/// Every segment links its target to its center. The state of one body relative to another is the sum of
/// the segment states from each body up to their nearest common ancestor, as in SPICE spkgeo: for example
/// the Moon relative to the Earth is (Moon wrt EMB) - (Earth wrt EMB), which never goes through the large
/// barycentric states that would cost precision.
///
/// Where several segments of one target cover an instant, the later segment in pack order wins (SPICE
/// precedence). All segments of one target must share one center, so the graph and the paths through it
/// do not depend on time and are computed once, when the chain is created.
///
/// A body with a data::PositionApproximation has no segments; it is linked to its proxy (its primary)
/// with a zero offset, and every state whose path uses that link is flagged approximate (ADR-0008).
///
/// A non-owning view of the segments (usually those of a data::DataPack), which must outlive it. Immutable
/// after creation; safe to share between threads.
class EphemerisChain {
public:
    /// Builds the graph. Fails with ErrorCode::data_format for a malformed segment (see
    /// ChebyshevSegment::create), a segment whose target is its center, segments that form a cycle, or an
    /// approximation of a body that has segments, appears twice or whose proxy has no ephemeris; and with
    /// ErrorCode::unsupported if the segments of one target have different centers.
    [[nodiscard]] static Result<EphemerisChain>
    create(std::span<const data::EphemerisSegment> segments,
           std::span<const data::PositionApproximation> approximations = {});

    /// The chain of all segments and approximate positions of `pack`.
    [[nodiscard]] static Result<EphemerisChain> create(const data::DataPack& pack) {
        return create(pack.ephemeris_segments(), pack.position_approximations());
    }

    /// Not allowed: the chain would point into a temporary pack.
    static Result<EphemerisChain> create(data::DataPack&& pack) = delete;

    /// Every body in the graph (each target and center of a segment), sorted by id.
    [[nodiscard]] std::span<const catalog::BodyId> bodies() const noexcept { return ids_; }

    /// True if `body` is in the graph.
    [[nodiscard]] bool contains(catalog::BodyId body) const noexcept;

    /// The center of `body`'s segments; nothing for a root (a body that is never a target, such as the
    /// solar system barycenter) or a body that is not in the graph.
    [[nodiscard]] std::optional<catalog::BodyId> parent(catalog::BodyId body) const noexcept;

    /// The path from `body` to its root: `body`, its parent, the parent's parent, ..., the root. Fails with
    /// ErrorCode::not_found if `body` is not in the graph.
    [[nodiscard]] Result<std::vector<catalog::BodyId>> path(catalog::BodyId body) const;

    /// The segments that give `body` relative to its parent, highest precedence first. Empty for a root, an
    /// approximated body or an unknown body.
    [[nodiscard]] std::vector<const ChebyshevSegment*> segments_of(catalog::BodyId body) const;

    /// The approximation that places `body` at its primary, or nullptr. Points into the approximations the
    /// chain was created from.
    [[nodiscard]] const data::PositionApproximation* approximation(catalog::BodyId body) const noexcept;

    /// True if states of `target` relative to `center` are approximate: the path between them uses the
    /// link of an approximated body. False for unknown or unconnected bodies.
    [[nodiscard]] bool is_approximate(catalog::BodyId target, catalog::BodyId center) const noexcept;

    /// When the state of `body` relative to its root is available: the intersection of the segment
    /// coverages along its path (FR-SIM-03). A root is covered always, which is represented as the span of
    /// all segments. Fails with ErrorCode::not_found if `body` is not in the graph.
    [[nodiscard]] Result<Coverage> coverage(catalog::BodyId body) const;

    /// When the state of `target` relative to `center` is available: the intersection of the segment
    /// coverages on both paths up to their common ancestor. Fails with ErrorCode::not_found if a body is not
    /// in the graph or the two are not connected.
    [[nodiscard]] Result<Coverage> coverage(catalog::BodyId target, catalog::BodyId center) const;

    /// The state of `target` relative to `center` at `t` (TDB), in `frame`: position in km, velocity in
    /// km/s. Never extrapolates (FR-SIM-03). Fails with ErrorCode::out_of_coverage outside the data,
    /// ErrorCode::not_found for an unknown or unconnected body, ErrorCode::invalid_argument for an invalid
    /// instant and ErrorCode::unsupported for body-fixed frames (until P4-03).
    [[nodiscard]] Result<frames::StateVector> state(catalog::BodyId target, catalog::BodyId center,
                                                    frames::FrameId frame, time::Instant t) const;

    /// The same query without allocating, for per-frame use. An invalid instant gives out_of_coverage.
    [[nodiscard]] Evaluation evaluate(catalog::BodyId target, catalog::BodyId center, frames::FrameId frame,
                                      time::Instant t) const noexcept;

private:
    static constexpr std::uint32_t k_no_parent = UINT32_MAX;

    struct Node {
        std::uint32_t parent = k_no_parent; // index into nodes_
        std::uint32_t depth = 0;            // number of segments from the node to its root
        std::uint32_t first_segment = 0;    // range in order_: this node's segments, highest precedence first
        std::uint32_t segment_count = 0;
        const data::PositionApproximation* approximation = nullptr; // set: placed at the parent, no segments
        Coverage own_coverage;  // union of this node's segments (everything if approximated)
        Coverage root_coverage; // when the state relative to the root is available
    };

    EphemerisChain() = default;

    // Index of `body` in ids_ and nodes_, or k_no_parent.
    [[nodiscard]] std::uint32_t find(catalog::BodyId body) const noexcept;

    // State of node `n` relative to its parent at `t`, in the ICRF.
    [[nodiscard]] std::optional<frames::StateVector> step(std::uint32_t n, time::Instant t) const noexcept;

    std::vector<ChebyshevSegment> segments_; // pack order
    std::vector<std::uint32_t> order_;       // segment indices grouped by node, highest precedence first
    std::vector<catalog::BodyId> ids_;       // sorted; parallel to nodes_
    std::vector<Node> nodes_;
    Coverage all_; // the span of every segment, for roots
    math::Mat3 icrf_to_ecliptic_;
    math::Mat3 ecliptic_to_icrf_;
};

} // namespace usim::ephemeris
