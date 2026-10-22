#include "usim/sim/orbit_path.hpp"

#include <cmath>
#include <optional>
#include <string>
#include <utility>

namespace usim::sim {

namespace {

// Angle between two position vectors as seen from the center, rad, in [0, pi].
double angle_between(const math::Vec3& a, const math::Vec3& b) noexcept {
    return std::atan2(math::norm(math::cross(a, b)), math::dot(a, b));
}

// Largest angle one marching step may sweep while looking for a revolution; small enough that the swept
// angle grows monotonically within a step.
constexpr double k_march_step_rad = 10.0 * math::k_pi / 180.0;

// Bisection steps that place the end of a revolution: the step is halved each time, so 40 steps resolve
// it far below the sampling.
constexpr int k_bisection_steps = 40;

// Deepest refinement of one initial interval.
constexpr int k_max_depth = 32;

class Sampler {
public:
    Sampler(const ephemeris::EphemerisChain& chain, catalog::BodyId body, catalog::BodyId center,
            frames::FrameId frame)
        : chain_(chain), body_(body), center_(center), frame_(frame) {}

    // The position at t, or the chain's error.
    Result<math::Vec3> position(time::Instant t) const {
        const ephemeris::Evaluation e = chain_.evaluate(body_, center_, frame_, t);
        if (e.ok()) {
            return e.state.position_km;
        }
        Result<frames::StateVector> detailed = chain_.state(body_, center_, frame_, t);
        return detailed ? make_error(ErrorCode::internal, "inconsistent ephemeris evaluation")
                        : detailed.error();
    }

    // Marches from t0 in `direction` (+1 or -1) until the swept angle reaches `target_rad` or `limit` is
    // reached. Returns the instant and the angle swept.
    Result<std::pair<time::Instant, double>> march(time::Instant t0, double direction, double target_rad,
                                                   time::Instant limit) const {
        Result<math::Vec3> start = position(t0);
        if (!start) {
            return start.error();
        }
        math::Vec3 r = start.value();
        time::Instant t = t0;
        double swept = 0.0;
        double dt = 3600.0;
        for (int i = 0; i < 1'000'000 && swept < target_rad; ++i) {
            const double to_limit = std::fabs(limit.seconds_since(t));
            if (to_limit <= 0.0) {
                break;
            }
            const double step = std::fmin(dt, to_limit);
            // A step to the limit lands on it exactly: t + (limit - t) can round past the coverage.
            const time::Instant next = step == to_limit ? limit : t.plus_seconds(direction * step);
            Result<math::Vec3> r_next = position(next);
            if (!r_next) {
                return r_next.error();
            }
            const double a = angle_between(r, r_next.value());
            if (a > k_march_step_rad && step > 1.0) {
                dt = step / 2.0; // too far: retry with a shorter step
                continue;
            }
            if (swept + a >= target_rad) {
                // The target lies in this step: bisect for the instant where it is reached.
                double lo = 0.0;
                double hi = step;
                for (int k = 0; k < k_bisection_steps; ++k) {
                    const double mid = 0.5 * (lo + hi);
                    Result<math::Vec3> r_mid = position(t.plus_seconds(direction * mid));
                    if (!r_mid) {
                        return r_mid.error();
                    }
                    if (swept + angle_between(r, r_mid.value()) < target_rad) {
                        lo = mid;
                    } else {
                        hi = mid;
                    }
                }
                return std::pair{t.plus_seconds(direction * hi), target_rad};
            }
            swept += a;
            t = next;
            r = r_next.value();
            if (a < k_march_step_rad / 4.0) {
                dt = step * 2.0;
            }
        }
        return std::pair{t, swept};
    }

    struct Point {
        time::Instant t;
        math::Vec3 r;
    };

    // Appends the refined points after `a` up to and including `b`. `pending` points will still be appended
    // by the callers afterwards; a split adds one point, so it happens only while the total stays within
    // `budget`.
    Result<void> refine(const Point& a, const Point& b, int depth, std::size_t pending, std::size_t budget,
                        const OrbitPathOptions& options, std::vector<OrbitPathPoint>& out) const {
        const time::Instant mid_t = a.t.plus_seconds(0.5 * b.t.seconds_since(a.t));
        if (depth < k_max_depth && out.size() + pending + 2 <= budget) {
            Result<math::Vec3> mid = position(mid_t);
            if (!mid) {
                return mid.error();
            }
            const math::Vec3 chord_mid = (a.r + b.r) * 0.5;
            const double error = math::norm(mid.value() - chord_mid);
            const bool fine = angle_between(a.r, b.r) <= options.max_angle_rad &&
                              error <= options.max_chord_error * math::norm(mid.value());
            if (!fine) {
                const Point m{mid_t, mid.value()};
                if (Result<void> left = refine(a, m, depth + 1, pending + 1, budget, options, out); !left) {
                    return left;
                }
                return refine(m, b, depth + 1, pending, budget, options, out);
            }
        }
        out.push_back({b.t, b.r});
        return {};
    }

private:
    const ephemeris::EphemerisChain& chain_;
    catalog::BodyId body_;
    catalog::BodyId center_;
    frames::FrameId frame_;
};

Result<void> check_request(catalog::BodyId body, catalog::BodyId center, frames::FrameId frame,
                           const OrbitPathOptions& options) {
    if (body == center) {
        return make_error(ErrorCode::invalid_argument, "an orbit path needs a center other than the body");
    }
    if (!frame.is_inertial()) {
        return make_error(ErrorCode::invalid_argument, "orbit paths are given in the ICRF or ECLIPJ2000");
    }
    if (!(options.max_angle_rad > 0.0) || !std::isfinite(options.max_angle_rad) ||
        !(options.max_chord_error > 0.0) || !std::isfinite(options.max_chord_error) ||
        options.initial_intervals == 0 || options.max_points < options.initial_intervals + 1) {
        return make_error(ErrorCode::invalid_argument, "invalid orbit path options");
    }
    return {};
}

} // namespace

Result<OrbitPath> sample_path(const ephemeris::EphemerisChain& chain, catalog::BodyId body,
                              catalog::BodyId center, frames::FrameId frame, time::Instant start,
                              time::Instant end, const OrbitPathOptions& options) {
    if (Result<void> ok = check_request(body, center, frame, options); !ok) {
        return ok.error();
    }
    if (!start.is_valid() || !end.is_valid() || !(start < end)) {
        return make_error(ErrorCode::invalid_argument, "an orbit path needs a window with start before end");
    }
    const Sampler sampler(chain, body, center, frame);
    OrbitPath path;
    path.body = body;
    path.center = center;
    path.frame = frame;
    path.start = start;
    path.end = end;
    path.approximate = chain.is_approximate(body, center);

    const double duration = end.seconds_since(start);
    const std::size_t n = options.initial_intervals;
    std::vector<Sampler::Point> initial;
    for (std::size_t k = 0; k <= n; ++k) {
        const time::Instant t =
            k == n ? end : start.plus_seconds(duration * static_cast<double>(k) / static_cast<double>(n));
        Result<math::Vec3> r = sampler.position(t);
        if (!r) {
            return r.error();
        }
        initial.push_back({t, r.value()});
    }
    // Refinement stops early enough that the endpoints of the later initial intervals still fit in
    // max_points.
    path.points.push_back({initial[0].t, initial[0].r});
    for (std::size_t k = 0; k < n; ++k) {
        const std::size_t budget = options.max_points - (n - k - 1);
        if (Result<void> ok = sampler.refine(initial[k], initial[k + 1], 0, 0, budget, options, path.points);
            !ok) {
            return ok.error();
        }
    }
    return path;
}

Result<OrbitPath> sample_revolution(const ephemeris::EphemerisChain& chain, catalog::BodyId body,
                                    catalog::BodyId center, frames::FrameId frame, time::Instant t,
                                    ephemeris::TimeInterval coverage, const OrbitPathOptions& options) {
    if (Result<void> ok = check_request(body, center, frame, options); !ok) {
        return ok.error();
    }
    if (!t.is_valid() || !coverage.contains(t)) {
        return make_error(ErrorCode::out_of_coverage,
                          "the instant of an orbit path must lie in the coverage");
    }
    const Sampler sampler(chain, body, center, frame);
    constexpr double k_turn = 2.0 * math::k_pi;
    // Half a turn ahead, then the rest behind; if the start of the coverage cuts that short, more ahead.
    Result<std::pair<time::Instant, double>> ahead = sampler.march(t, 1.0, 0.5 * k_turn, coverage.end);
    if (!ahead) {
        return ahead.error();
    }
    Result<std::pair<time::Instant, double>> behind =
        sampler.march(t, -1.0, k_turn - ahead.value().second, coverage.start);
    if (!behind) {
        return behind.error();
    }
    time::Instant end = ahead.value().first;
    double swept = ahead.value().second + behind.value().second;
    if (swept < k_turn) {
        Result<std::pair<time::Instant, double>> more = sampler.march(end, 1.0, k_turn - swept, coverage.end);
        if (!more) {
            return more.error();
        }
        end = more.value().first;
        swept += more.value().second;
    }
    Result<OrbitPath> path = sample_path(chain, body, center, frame, behind.value().first, end, options);
    if (path) {
        path.value().full_revolution = swept >= k_turn * (1.0 - 1.0e-12);
    }
    return path;
}

OrbitPathCache::OrbitPathCache(const ephemeris::EphemerisChain& chain, ephemeris::TimeInterval coverage,
                               OrbitPathOptions options, double reuse_fraction)
    : chain_(&chain), coverage_(coverage), options_(options), reuse_fraction_(reuse_fraction) {}

Result<const OrbitPath*> OrbitPathCache::revolution(catalog::BodyId body, catalog::BodyId center,
                                                    frames::FrameId frame, time::Instant t) {
    Entry* entry = nullptr;
    for (Entry& e : entries_) {
        if (e.path.body == body && e.path.center == center && e.path.frame == frame) {
            entry = &e;
            break;
        }
    }
    if (entry != nullptr && t.is_valid()) {
        const double window = entry->path.end.seconds_since(entry->path.start);
        if (std::fabs(t.seconds_since(entry->sampled_for)) <= reuse_fraction_ * window) {
            return &entry->path;
        }
    }
    Result<OrbitPath> path = sample_revolution(*chain_, body, center, frame, t, coverage_, options_);
    if (!path) {
        return path.error();
    }
    ++samplings_;
    if (entry == nullptr) {
        entries_.push_back({t, std::move(path).value()});
        return &entries_.back().path;
    }
    *entry = Entry{t, std::move(path).value()};
    return &entry->path;
}

} // namespace usim::sim
