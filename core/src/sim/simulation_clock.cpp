#include "usim/sim/simulation_clock.hpp"

#include <cmath>

namespace usim::sim {

SimulationClock::SimulationClock(ephemeris::TimeInterval limits, time::Instant start) noexcept
    : limits_(limits), now_(limits.start) {
    if (start.is_valid()) {
        move_to(start);
    }
    limit_ = ClockLimit::none;
}

ClockLimit SimulationClock::move_to(time::Instant t) noexcept {
    if (t < limits_.start) {
        now_ = limits_.start;
        limit_ = ClockLimit::start;
    } else if (limits_.end < t) {
        now_ = limits_.end;
        limit_ = ClockLimit::end;
    } else {
        now_ = t;
        limit_ = ClockLimit::none;
    }
    return limit_;
}

Result<ClockLimit> SimulationClock::advance(double real_seconds) {
    if (!std::isfinite(real_seconds) || real_seconds < 0.0) {
        return make_error(ErrorCode::invalid_argument, "elapsed real time must be finite and not negative");
    }
    if (paused_) {
        return limit_;
    }
    const double delta_s = real_seconds * rate_;
    const time::Instant target = now_.plus_seconds(delta_s);
    if (!target.is_valid()) {
        // The step overflowed or left the representable range: it goes past the limit in its direction.
        now_ = delta_s < 0.0 ? limits_.start : limits_.end;
        limit_ = delta_s < 0.0 ? ClockLimit::start : ClockLimit::end;
        return limit_;
    }
    return move_to(target);
}

Result<ClockLimit> SimulationClock::set_time(time::Instant t) {
    if (!t.is_valid()) {
        return make_error(ErrorCode::invalid_argument, "invalid instant");
    }
    return move_to(t);
}

Result<void> SimulationClock::set_rate(double rate) {
    if (!std::isfinite(rate)) {
        return make_error(ErrorCode::invalid_argument, "the clock rate must be finite");
    }
    rate_ = rate;
    return {};
}

} // namespace usim::sim
