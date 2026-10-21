#include "usim/math/chebyshev.hpp"

#include "fixtures/spice_reference.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <array>
#include <cmath>
#include <vector>

using Catch::Matchers::WithinAbs;
namespace m = usim::math;
namespace spice = usim::test::spice;

TEST_CASE("Chebyshev evaluation matches CSPICE chbint", "[math][chebyshev]") {
    for (const auto& c : spice::k_chebyshev_cases) {
        const m::ChebyshevValue r = m::chebyshev_on_interval(spice::k_chebyshev_coefficients,
                                                             spice::k_chebyshev_midpoint, spice::k_chebyshev_radius, c.x);
        CHECK_THAT(r.value, WithinAbs(c.value, 1e-14));
        CHECK_THAT(r.derivative, WithinAbs(c.derivative, 1e-13));
    }
}

TEST_CASE("each basis polynomial matches its closed form", "[math][chebyshev]") {
    // T_k(cos t) = cos(k t) and T_k'(cos t) = k sin(k t) / sin(t).
    for (std::size_t k = 0; k <= 15; ++k) {
        std::vector<double> coefficients(k + 1, 0.0);
        coefficients[k] = 1.0;
        for (const double s : {-0.95, -0.5, -0.1, 0.0, 0.3, 0.77, 0.99}) {
            const double t = std::acos(s);
            const double kd = static_cast<double>(k);
            const double expected_value = std::cos(kd * t);
            const double expected_derivative = kd * std::sin(kd * t) / std::sin(t);
            CHECK_THAT(m::chebyshev(coefficients, s), WithinAbs(expected_value, 1e-14));
            const m::ChebyshevValue r = m::chebyshev_with_derivative(coefficients, s);
            CHECK_THAT(r.value, WithinAbs(expected_value, 1e-14));
            CHECK_THAT(r.derivative, WithinAbs(expected_derivative, 1e-12));
        }
    }
}

TEST_CASE("low-degree series and edge cases", "[math][chebyshev]") {
    // 2 T0 + 3 T1 - T2 + 0.5 T3 = 2 + 3s - (2s^2 - 1) + 0.5 (4s^3 - 3s) = 3 + 1.5s - 2s^2 + 2s^3
    const std::array<double, 4> c{2.0, 3.0, -1.0, 0.5};
    for (const double s : {-1.0, -0.4, 0.0, 0.6, 1.0}) {
        const double value = 3.0 + 1.5 * s - 2.0 * s * s + 2.0 * s * s * s;
        const double derivative = 1.5 - 4.0 * s + 6.0 * s * s;
        CHECK_THAT(m::chebyshev(c, s), WithinAbs(value, 1e-15));
        CHECK_THAT(m::chebyshev_with_derivative(c, s).derivative, WithinAbs(derivative, 1e-14));
    }
    CHECK(m::chebyshev({}, 0.5) == 0.0);
    CHECK(m::chebyshev_with_derivative({}, 0.5).derivative == 0.0);
    const std::array<double, 1> constant{4.25};
    CHECK(m::chebyshev(constant, -0.3) == 4.25);
    CHECK(m::chebyshev_with_derivative(constant, -0.3).derivative == 0.0);
}
