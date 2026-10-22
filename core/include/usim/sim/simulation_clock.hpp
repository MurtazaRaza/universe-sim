#pragma once

#include "usim/ephemeris/coverage.hpp"
#include "usim/result.hpp"
#include "usim/time/time_point.hpp"

#include <cstdint>

namespace usim::sim {

/// Where the clock stands relative to its limits. Values are stable and shared with the C ABI.
enum class ClockLimit : std::int32_t {
    none = 0,  ///< Strictly inside the limits, or the last change did not reach one.
    start = 1, ///< Held at the start limit: a change would have moved it earlier.
    end = 2,   ///< Held at the end limit: a change would have moved it later.
};

/// The simulation clock (SPEC FR-SIM-05): the current instant (TDB), a signed rate, pause, setting the
/// time, and clamping to the instants the data covers. It lives in the core so every host advances time
/// the same way; a host only reports how much real time passed.
///
/// The clock never leaves its limits. A change that would cross a limit stops at the limit and records
/// which one (limit()); the rate and the pause state are kept, so reversing the rate moves away again.
/// Not thread-safe: change it from one thread.
class SimulationClock {
public:
    /// A clock limited to `limits`, at `start` (clamped into the limits), running at rate 1 (one simulated
    /// second per real second), not paused. Precondition: `limits` is a valid interval (start <= end).
    SimulationClock(ephemeris::TimeInterval limits, time::Instant start) noexcept;

    /// The current instant (TDB).
    [[nodiscard]] time::Instant now() const noexcept { return now_; }

    /// Simulated seconds per real second. Negative runs time backward; 0 holds it.
    [[nodiscard]] double rate() const noexcept { return rate_; }

    /// True while paused: advance() leaves the time unchanged.
    [[nodiscard]] bool paused() const noexcept { return paused_; }

    /// The instants the clock may take.
    [[nodiscard]] ephemeris::TimeInterval limits() const noexcept { return limits_; }

    /// Whether the last change of the time stopped at a limit. Reset by any change that does not.
    [[nodiscard]] ClockLimit limit() const noexcept { return limit_; }

    /// Moves the time by `real_seconds * rate()` unless paused, stopping at a limit. Returns the limit
    /// reached (also kept in limit()). Fails with ErrorCode::invalid_argument, changing nothing, if
    /// `real_seconds` is negative or not finite. Allocates only on failure (the message).
    Result<ClockLimit> advance(double real_seconds);

    /// Sets the time, clamped to the limits. Returns the limit reached. Fails with
    /// ErrorCode::invalid_argument, changing nothing, if `t` is invalid.
    Result<ClockLimit> set_time(time::Instant t);

    /// Sets the rate. Fails with ErrorCode::invalid_argument, changing nothing, if `rate` is not finite.
    Result<void> set_rate(double rate);

    /// Pauses or resumes.
    void set_paused(bool paused) noexcept { paused_ = paused; }

private:
    // Clamps `t` to the limits, stores it and records the limit reached.
    ClockLimit move_to(time::Instant t) noexcept;

    ephemeris::TimeInterval limits_;
    time::Instant now_;
    double rate_ = 1.0;
    bool paused_ = false;
    ClockLimit limit_ = ClockLimit::none;
};

} // namespace usim::sim
