#include "usim/math/chebyshev.hpp"

#include <cstddef>

namespace usim::math {

// Clenshaw: with b_{n} = b_{n+1} = 0 and b_k = c_k + 2 s b_{k+1} - b_{k+2} for k = n-1 down to 1,
// f(s) = c_0 + s b_1 - b_2. Differentiating gives b'_k = 2 b_{k+1} + 2 s b'_{k+1} - b'_{k+2} and
// f'(s) = b_1 + s b'_1 - b'_2.

double chebyshev(std::span<const double> coefficients, double s) noexcept {
    const std::size_t n = coefficients.size();
    if (n == 0) {
        return 0.0;
    }
    const double two_s = 2.0 * s;
    double b1 = 0.0; // b_{k+1}
    double b2 = 0.0; // b_{k+2}
    for (std::size_t k = n - 1; k >= 1; --k) {
        const double b0 = coefficients[k] + two_s * b1 - b2;
        b2 = b1;
        b1 = b0;
    }
    return coefficients[0] + s * b1 - b2;
}

ChebyshevValue chebyshev_with_derivative(std::span<const double> coefficients, double s) noexcept {
    const std::size_t n = coefficients.size();
    if (n == 0) {
        return {0.0, 0.0};
    }
    const double two_s = 2.0 * s;
    double b1 = 0.0;
    double b2 = 0.0;
    double d1 = 0.0; // b'_{k+1}
    double d2 = 0.0; // b'_{k+2}
    for (std::size_t k = n - 1; k >= 1; --k) {
        const double d0 = 2.0 * b1 + two_s * d1 - d2;
        const double b0 = coefficients[k] + two_s * b1 - b2;
        d2 = d1;
        d1 = d0;
        b2 = b1;
        b1 = b0;
    }
    return {coefficients[0] + s * b1 - b2, b1 + s * d1 - d2};
}

ChebyshevValue chebyshev_on_interval(std::span<const double> coefficients, double midpoint, double radius,
                                     double x) noexcept {
    const ChebyshevValue r = chebyshev_with_derivative(coefficients, (x - midpoint) / radius);
    return {r.value, r.derivative / radius};
}

} // namespace usim::math
