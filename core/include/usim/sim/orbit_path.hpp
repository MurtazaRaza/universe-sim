#pragma once

#include "usim/catalog/body_id.hpp"
#include "usim/ephemeris/ephemeris_chain.hpp"
#include "usim/frames/frame_id.hpp"
#include "usim/math/angle.hpp"
#include "usim/math/vec3.hpp"
#include "usim/result.hpp"
#include "usim/time/time_point.hpp"

#include <cstddef>
#include <deque>
#include <vector>

namespace usim::sim {

/// How finely an orbit path is sampled (SPEC FR-SIM-06).
struct OrbitPathOptions {
    /// Largest angle between consecutive points as seen from the center, rad. Fast parts of an orbit
    /// (periapsis) get more points, because the same angle passes there in less time.
    double max_angle_rad = math::deg_to_rad(0.5);
    /// Largest distance of the true midpoint of two consecutive points from their chord, relative to the
    /// distance from the center.
    double max_chord_error = 1.0e-4;
    /// Uniform intervals the window is first divided into.
    std::size_t initial_intervals = 32;
    /// Most points a path may have.
    std::size_t max_points = 8192;
};

/// One sample of a path.
struct OrbitPathPoint {
    time::Instant t;        ///< The instant (TDB).
    math::Vec3 position_km; ///< Position relative to the path's center, in the path's frame, km.
};

/// A sampled path (FR-SIM-06): positions of a body relative to a center over a window, in time order.
struct OrbitPath {
    catalog::BodyId body;                                  ///< The body.
    catalog::BodyId center;                                ///< What the positions are relative to.
    frames::FrameId frame = frames::FrameId::eclipj2000(); ///< The frame of the positions (inertial).
    time::Instant start;                                   ///< First instant (TDB).
    time::Instant end;                                     ///< Last instant (TDB).
    std::vector<OrbitPathPoint> points;                    ///< The samples, start and end included.
    bool full_revolution = false; ///< The window spans one full turn around the center.
    bool approximate = false;     ///< The positions are approximate (ADR-0008).
};

/// Samples the path of `body` relative to `center` in `frame` (inertial) over [start, end] (TDB),
/// refining intervals until consecutive points satisfy `options`. Fails like EphemerisChain::state if a
/// position is unavailable anywhere in the window, and with ErrorCode::invalid_argument for an empty or
/// invalid window or options.
[[nodiscard]] Result<OrbitPath> sample_path(const ephemeris::EphemerisChain& chain, catalog::BodyId body,
                                            catalog::BodyId center, frames::FrameId frame,
                                            time::Instant start, time::Instant end,
                                            const OrbitPathOptions& options = {});

/// Samples one revolution of `body` around `center`, centered on `t` where the data allows: the window
/// runs from when the body was half a turn behind to when it is half a turn ahead, as swept around the
/// center, measured from the ephemeris (no orbital elements). Near the ends of `coverage` the window is
/// shifted to stay inside, and if the coverage is shorter than one turn the path covers what there is
/// (full_revolution is false).
[[nodiscard]] Result<OrbitPath> sample_revolution(const ephemeris::EphemerisChain& chain,
                                                  catalog::BodyId body, catalog::BodyId center,
                                                  frames::FrameId frame, time::Instant t,
                                                  ephemeris::TimeInterval coverage,
                                                  const OrbitPathOptions& options = {});

/// Keeps the last revolution sampled per body, center and frame, and samples again only when asked for an
/// instant that has moved more than `reuse_fraction` of the window from the instant it was sampled for
/// (a large time jump, or slow drift). Not thread-safe.
class OrbitPathCache {
public:
    /// A cache over `chain`, which must outlive it.
    explicit OrbitPathCache(const ephemeris::EphemerisChain& chain, ephemeris::TimeInterval coverage,
                            OrbitPathOptions options = {}, double reuse_fraction = 0.02);

    /// The revolution of `body` around `center` for the instant `t`, sampled or from the cache. The
    /// pointer stays valid until the next call for the same body, center and frame, or clear().
    [[nodiscard]] Result<const OrbitPath*> revolution(catalog::BodyId body, catalog::BodyId center,
                                                      frames::FrameId frame, time::Instant t);

    /// Number of samplings done so far (for tests and diagnostics).
    [[nodiscard]] std::size_t samplings() const noexcept { return samplings_; }

    /// Drops every cached path.
    void clear() noexcept { entries_.clear(); }

private:
    struct Entry {
        time::Instant sampled_for;
        OrbitPath path;
    };

    const ephemeris::EphemerisChain* chain_;
    ephemeris::TimeInterval coverage_;
    OrbitPathOptions options_;
    double reuse_fraction_;
    std::deque<Entry> entries_; // a deque, so adding an entry keeps references to the others valid
    std::size_t samplings_ = 0;
};

} // namespace usim::sim
