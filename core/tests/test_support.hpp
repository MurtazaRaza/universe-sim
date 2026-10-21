#pragma once

// Helpers shared by the unit tests.

#include "usim/math/quat.hpp"
#include "usim/math/vec3.hpp"

#include <cmath>
#include <cstdint>
#include <limits>
#include <numbers>

namespace usim::test {

/// SplitMix64: a small generator whose output is identical on every platform (unlike the standard
/// distributions), so randomized tests fail the same way everywhere.
class SplitMix64 {
public:
    explicit SplitMix64(std::uint64_t seed) : state_(seed) {}

    std::uint64_t next_u64() {
        std::uint64_t z = (state_ += 0x9E3779B97F4A7C15ull);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ull;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBull;
        return z ^ (z >> 31);
    }

    /// Uniform in [0, 1).
    double next_double() { return static_cast<double>(next_u64() >> 11) * 0x1.0p-53; }

    /// Uniform in [lo, hi).
    double uniform(double lo, double hi) { return lo + (hi - lo) * next_double(); }

private:
    std::uint64_t state_;
};

/// A uniformly distributed random rotation (Shoemake 1992).
inline math::Quat random_rotation(SplitMix64& rng) {
    const double u1 = rng.next_double();
    const double u2 = rng.next_double() * 2.0 * std::numbers::pi;
    const double u3 = rng.next_double() * 2.0 * std::numbers::pi;
    const double a = std::sqrt(1.0 - u1);
    const double b = std::sqrt(u1);
    return {a * std::cos(u2), a * std::sin(u2), b * std::cos(u3), b * std::sin(u3)};
}

/// A vector with components uniform in [-scale, scale).
inline math::Vec3 random_vector(SplitMix64& rng, double scale) {
    return {rng.uniform(-scale, scale), rng.uniform(-scale, scale), rng.uniform(-scale, scale)};
}

/// Distance from |x| to the next larger double: the resolution of a double near x.
inline double ulp(double x) {
    const double a = std::fabs(x);
    return std::nextafter(a, std::numeric_limits<double>::infinity()) - a;
}

/// Largest absolute component difference between two vectors.
inline double max_abs_difference(const math::Vec3& a, const math::Vec3& b) {
    return std::fmax(std::fabs(a.x - b.x), std::fmax(std::fabs(a.y - b.y), std::fabs(a.z - b.z)));
}

} // namespace usim::test
