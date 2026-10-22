#pragma once

// Synthetic ephemeris segments for the unit tests. The coefficients are made up for the tests and are not
// physical data.

#include "usim/data/data_pack.hpp"
#include "usim/math/vec3.hpp"

#include <cmath>
#include <cstdint>
#include <functional>
#include <numbers>
#include <utility>
#include <vector>

namespace usim::test::ephemeris {

/// A segment of `count` records of `interval_s` from `init_s`, covering all of them. `coefficient(record,
/// component, k)` gives coefficient k of a component (0 to 2 position, 3 to 5 velocity for type 3).
inline data::EphemerisSegment
make_segment(std::int64_t target, std::int64_t center, std::int32_t spk_type, std::uint32_t degree,
             double init_s, double interval_s, std::uint32_t count,
             const std::function<double(std::uint32_t, std::uint32_t, std::uint32_t)>& coefficient) {
    data::EphemerisSegment s;
    s.target = catalog::BodyId::naif(target);
    s.center = catalog::BodyId::naif(center);
    s.spk_type = spk_type;
    s.init_tdb_s = init_s;
    s.interval_s = interval_s;
    s.start_tdb_s = init_s;
    s.end_tdb_s = init_s + interval_s * static_cast<double>(count);
    const std::uint32_t components = spk_type == 3 ? 6u : 3u;
    s.record_size = 2 + components * (degree + 1);
    s.record_count = count;
    for (std::uint32_t r = 0; r < count; ++r) {
        s.records.push_back(init_s + interval_s * (static_cast<double>(r) + 0.5));
        s.records.push_back(interval_s / 2.0);
        for (std::uint32_t c = 0; c < components; ++c) {
            for (std::uint32_t k = 0; k <= degree; ++k) {
                s.records.push_back(coefficient(r, c, k));
            }
        }
    }
    s.source = {"S-TEST", "synthetic segment " + std::to_string(target) + " wrt " + std::to_string(center)};
    return s;
}

/// A type 2 segment whose position is constant: `x`, `y`, `z` km in every record.
inline data::EphemerisSegment constant_segment(std::int64_t target, std::int64_t center, double x, double y,
                                               double z, double init_s = -1000.0, double interval_s = 500.0,
                                               std::uint32_t count = 4) {
    return make_segment(
        target, center, 2, 0, init_s, interval_s, count,
        [=](std::uint32_t, std::uint32_t c, std::uint32_t) { return c == 0 ? x : (c == 1 ? y : z); });
}

/// A type 2 segment that moves linearly: position (v t, 2 v t, 3 v t) km at TDB t seconds, so the velocity
/// is (v, 2 v, 3 v) km/s. In each record the position is c0 + c1 s with s = (t - MID) / RADIUS.
inline data::EphemerisSegment linear_segment(std::int64_t target, std::int64_t center, double v,
                                             double init_s = -1000.0, double interval_s = 500.0,
                                             std::uint32_t count = 4) {
    return make_segment(target, center, 2, 1, init_s, interval_s, count,
                        [=](std::uint32_t r, std::uint32_t c, std::uint32_t k) {
                            const double mid = init_s + interval_s * (static_cast<double>(r) + 0.5);
                            const double scale = v * static_cast<double>(c + 1);
                            return k == 0 ? scale * mid : scale * interval_s / 2.0;
                        });
}

/// A type 2 segment whose records interpolate `position(t)` (km at TDB seconds t) at the Chebyshev nodes of
/// each record: `count` records of `interval_s` from `init_s`, `degree` + 1 coefficients per component.
inline data::EphemerisSegment fitted_segment(std::int64_t target, std::int64_t center,
                                             const std::function<math::Vec3(double)>& position, double init_s,
                                             double interval_s, std::uint32_t count, std::uint32_t degree) {
    const std::uint32_t n = degree + 1;
    std::vector<std::vector<double>> coefficients(count); // per record: x, y, z coefficients
    for (std::uint32_t r = 0; r < count; ++r) {
        const double mid = init_s + interval_s * (static_cast<double>(r) + 0.5);
        const double radius = interval_s / 2.0;
        std::vector<math::Vec3> values(n);
        for (std::uint32_t j = 0; j < n; ++j) {
            values[j] = position(mid + radius * std::cos(std::numbers::pi * (j + 0.5) / n));
        }
        for (std::uint32_t c = 0; c < 3; ++c) {
            for (std::uint32_t k = 0; k < n; ++k) {
                double sum = 0.0;
                for (std::uint32_t j = 0; j < n; ++j) {
                    const double* v = &values[j].x;
                    sum += v[c] * std::cos(std::numbers::pi * k * (j + 0.5) / n);
                }
                coefficients[r].push_back((k == 0 ? 1.0 : 2.0) * sum / n);
            }
        }
    }
    return make_segment(
        target, center, 2, degree, init_s, interval_s, count,
        [&](std::uint32_t r, std::uint32_t c, std::uint32_t k) { return coefficients[r][c * n + k]; });
}

} // namespace usim::test::ephemeris
