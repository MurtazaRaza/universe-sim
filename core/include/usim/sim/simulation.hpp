#pragma once

#include "usim/catalog/body_catalog.hpp"
#include "usim/catalog/sky.hpp"
#include "usim/catalog/body_id.hpp"
#include "usim/data/data_pack.hpp"
#include "usim/ephemeris/coverage.hpp"
#include "usim/ephemeris/ephemeris_chain.hpp"
#include "usim/frames/frame_id.hpp"
#include "usim/frames/frame_system.hpp"
#include "usim/frames/frame_transform.hpp"
#include "usim/math/quat.hpp"
#include "usim/math/vec3.hpp"
#include "usim/presentation/view.hpp"
#include "usim/result.hpp"
#include "usim/sim/orbit_path.hpp"
#include "usim/sim/simulation_clock.hpp"
#include "usim/time/time_point.hpp"

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <memory>
#include <span>
#include <vector>

namespace usim::sim {

/// The model that produced a state (SPEC FR-SIM-04). Values are stable and shared with the C ABI.
enum class StateModel : std::int32_t {
    ephemeris = 0,  ///< Evaluated from ephemeris segments of the pack.
    analytic = 1,   ///< Two-body propagation (Phase 7).
    integrated = 2, ///< Numerical integration (Phases 7 and 8).
};

/// Quality metadata of a state (FR-SIM-04).
struct StateQuality {
    StateModel model = StateModel::ephemeris; ///< The model that produced the state.
    /// The path to the center uses a body placed at its primary (data::PositionApproximation, ADR-0008).
    bool approximate = false;
};

/// The state of a body relative to a center, in a frame, at an instant (FR-SIM-01).
struct BodyState {
    catalog::BodyId body;                            ///< The body.
    catalog::BodyId center;                          ///< What the state is relative to.
    frames::FrameId frame = frames::FrameId::icrf(); ///< The frame of the vectors.
    time::Instant t;                                 ///< The instant (TDB).
    frames::StateVector state;                       ///< Position in km, velocity in km/s.
    StateQuality quality;                            ///< How the state was obtained.
};

/// Flag bits of BodySnapshot::flags. Values are stable and shared with the C ABI.
enum SnapshotFlag : std::uint32_t {
    snapshot_approximate = 1u << 0,    ///< The position is approximate (ADR-0008).
    snapshot_no_position = 1u << 1,    ///< No data gives a position at this instant; render_position is zero.
    snapshot_no_orientation = 1u << 2, ///< No rotation model; orientation is the identity.
    snapshot_no_radius = 1u << 3,      ///< No mean radius; render_radius is zero.
    snapshot_compressed = 1u << 4,     ///< Beyond the view's compression distance: position and size scaled.
    snapshot_no_light = 1u << 5, ///< No direction to the light source (it is the light, or none exists).
};

/// What a snapshot shows and how (ARCHITECTURE section 5).
struct SnapshotRequest {
    /// Positions are taken relative to this body, usually the camera's focus, so that values near the
    /// camera are small.
    catalog::BodyId center = catalog::BodyId::naif(0);
    /// The display frame: frames::FrameId::icrf() or eclipj2000() (the default).
    frames::FrameId frame = frames::FrameId::eclipj2000();
    /// The view origin (km relative to `center`, in `frame`), render units, host axes and body scaling.
    presentation::View view;
};

/// One body of a snapshot, ready to render.
struct BodySnapshot {
    catalog::BodyId body;         ///< The body.
    math::Vec3 render_position{}; ///< Relative to the view origin, in render units and host axes.
    math::Quat orientation{};     ///< Rotation from the body-fixed frame to the display frame, host axes.
    /// Mean radius in render units. In ScaleMode::enlarged_bodies, planets, dwarf planets and moons are
    /// enlarged by View::body_scale, but never beyond 40 % of the distance between a planet and any of its
    /// moons, so a system never overlaps itself; the star keeps its true size (it would swallow the inner
    /// planets).
    double render_radius = 0.0;
    /// The radii along the body's own axes, as lengths along the host's axes in render units
    /// (presentation::to_render_radii): scale the body's mesh by these, then apply `orientation`. The mean
    /// radius on every axis when the pack has no triaxial radii; zero without any radius.
    math::Vec3 render_radii{};
    /// Unit vector from the body toward the light source (the catalog's star), host axes, computed from
    /// the true positions; lights the body correctly even when its position is compressed. Zero with
    /// snapshot_no_light.
    math::Vec3 light_direction{};
    std::uint32_t flags = 0; ///< SnapshotFlag bits.
};

/// StarSnapshot flags.
enum StarFlag : std::uint32_t {
    star_has_color = 1u << 0, ///< The catalog gives a B-V color index.
};

/// One star of the sky background, ready to render (SPEC FR-REN-09).
struct StarSnapshot {
    /// Unit vector toward the star at the clock's time, in the display frame and the host's axes. Stars are
    /// treated as infinitely far away: the same direction from every point of the Solar System.
    math::Vec3 direction{};
    double v_magnitude = 0.0; ///< V magnitude.
    double b_minus_v = 0.0;   ///< B-V color index, magnitudes; 0 without star_has_color.
    std::uint32_t flags = 0;  ///< StarFlag bits.
};

/// The facade hosts use (ARCHITECTURE section 3.4): a loaded pack with its catalog, ephemeris, frames and
/// clock, answering state and orientation queries and filling per-frame snapshots.
///
/// Const member functions may be called from several threads at once; changing the clock may not happen
/// at the same time as any other call.
class Simulation {
public:
    /// A simulation over `pack`. The clock runs at rate 1 from J2000 (TDB), clamped into coverage(); the
    /// core reads no wall clock, so a host sets the current time itself. Fails with
    /// ErrorCode::data_format if the pack's ephemeris cannot be assembled or covers no common time span.
    [[nodiscard]] static Result<Simulation> create(data::DataPack pack);

    /// Parses `bytes` as a pack (data::DataPack::parse) and creates a simulation over it.
    [[nodiscard]] static Result<Simulation> load(std::span<const std::byte> bytes);

    /// Loads the pack file at `path` (data::DataPack::load_file) and creates a simulation over it.
    [[nodiscard]] static Result<Simulation> load_file(const std::filesystem::path& path);

    /// The pack.
    [[nodiscard]] const data::DataPack& pack() const noexcept { return *pack_; }

    /// The catalog: bodies, hierarchy, sourced values.
    [[nodiscard]] const catalog::BodyCatalog& catalog() const noexcept { return pack_->catalog(); }

    /// The ephemeris graph.
    [[nodiscard]] const ephemeris::EphemerisChain& ephemeris() const noexcept { return chain_; }

    /// The frames, including the body-fixed frames of the pack's rotation models.
    [[nodiscard]] const frames::FrameSystem& frames() const noexcept { return frames_; }

    /// The span in which every body with a position is covered: the clock's limits (FR-SIM-03).
    [[nodiscard]] ephemeris::TimeInterval coverage() const noexcept { return coverage_; }

    /// The clock.
    [[nodiscard]] SimulationClock& clock() noexcept { return clock_; }

    /// The clock.
    [[nodiscard]] const SimulationClock& clock() const noexcept { return clock_; }

    /// The state of `body` relative to `center` in `frame` at `t` (FR-SIM-01). Body-fixed frames include
    /// the velocity of the rotating frame. Fails like ephemeris::EphemerisChain::state, and with
    /// ErrorCode::not_found for a body-fixed frame without a rotation model.
    [[nodiscard]] Result<BodyState> state(catalog::BodyId body, catalog::BodyId center, frames::FrameId frame,
                                          time::Instant t) const;

    /// The orientation of `body` at `t` (FR-SIM-02): the rotation from its body-fixed frame to `frame`.
    /// Fails with ErrorCode::not_found if the body has no rotation model or `frame` is not available.
    [[nodiscard]] Result<math::Quat> orientation(catalog::BodyId body, frames::FrameId frame,
                                                 time::Instant t) const;

    /// One revolution of `body` around `center` in `frame` (inertial), centered on `t` (FR-SIM-06; see
    /// sample_revolution). For the clock's time pass clock().now().
    [[nodiscard]] Result<OrbitPath> orbit_path(catalog::BodyId body, catalog::BodyId center,
                                               frames::FrameId frame, time::Instant t,
                                               const OrbitPathOptions& options = {}) const {
        return sample_revolution(chain_, body, center, frame, t, coverage_, options);
    }

    /// The body an orbit line of `body` is usually drawn around, from the catalog hierarchy: for a moon,
    /// the planet of its system (the planet or dwarf planet with the same primary); for the star, the
    /// root; for any other body, the star (the root's child of kind star). Fails with
    /// ErrorCode::not_found if the body or that center is not in the catalog.
    [[nodiscard]] Result<catalog::BodyId> default_orbit_center(catalog::BodyId body) const;

    /// The body that lights the others: the first catalog body of kind star. Invalid if there is none.
    [[nodiscard]] catalog::BodyId light_source() const noexcept { return light_; }

    /// Number of bodies in a full snapshot: every catalog body.
    [[nodiscard]] std::size_t snapshot_size() const noexcept { return bodies_.size(); }

    /// Fills `out` with the catalog bodies from index `first` on, in catalog order, at the clock's current
    /// time, and returns how many it wrote (all remaining ones, or as many as fit). Allocates nothing for a
    /// valid request (NFR-03). Fails with ErrorCode::invalid_argument for an invalid view, a body-fixed
    /// frame or `first` past the end, and with ErrorCode::not_found if the center has no ephemeris.
    [[nodiscard]] Result<std::size_t> fill_snapshot(const SnapshotRequest& request,
                                                    std::span<BodySnapshot> out, std::size_t first = 0) const;

    /// The star catalog of the sky background (empty without one).
    [[nodiscard]] const catalog::StarCatalog& stars() const noexcept { return pack_->star_catalog(); }

    /// Ring regions and gaps, sorted by body and inner radius (empty without rings).
    [[nodiscard]] std::span<const catalog::Ring> rings() const noexcept { return pack_->rings(); }

    /// Fills `out` with the stars from index `first` on, in catalog order, at the clock's current time, and
    /// returns how many it wrote. Uses the request's display frame and the view's host axes; the center,
    /// origin and scale do not apply to directions. Allocates nothing. Fails with ErrorCode::invalid_argument
    /// for a body-fixed frame or `first` past the end.
    [[nodiscard]] Result<std::size_t> fill_stars(const SnapshotRequest& request, std::span<StarSnapshot> out,
                                                 std::size_t first = 0) const;

private:
    // What a snapshot needs of each catalog body, gathered once.
    struct SnapshotBody {
        catalog::BodyId id;
        bool has_radius = false;
        double radius_km = 0.0;
        math::Vec3 radii_km{}; // a, b, c, or the mean radius three times
        double max_radius_km = 0.0;
        bool has_rotation = false;
        bool enlarge = false; // planets, dwarf planets and moons; not the star
        std::vector<std::uint32_t>
            partners; // a planet's moons, or a moon's planet (kept apart when enlarged)
    };

    Simulation(std::unique_ptr<const data::DataPack> pack, ephemeris::EphemerisChain chain,
               frames::FrameSystem frames, ephemeris::TimeInterval coverage,
               std::vector<SnapshotBody> bodies);

    std::unique_ptr<const data::DataPack> pack_; // owned at a fixed address: chain_ points into it
    ephemeris::EphemerisChain chain_;
    frames::FrameSystem frames_;
    ephemeris::TimeInterval coverage_;
    SimulationClock clock_;
    std::vector<SnapshotBody> bodies_;
    catalog::BodyId light_;
};

} // namespace usim::sim
