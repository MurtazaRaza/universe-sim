#pragma once

#include <span>

namespace usim::math {

/// Value of a Chebyshev series and its derivative.
struct ChebyshevValue {
    double value;      ///< f(x).
    double derivative; ///< df/dx, in units of f per unit of x.
};

/// Evaluates f(s) = sum over k of coefficients[k] * T_k(s), with T_k the Chebyshev polynomials of the
/// first kind, using the Clenshaw recurrence. `s` is the normalized argument and should lie in [-1, 1].
/// An empty coefficient list gives 0.
[[nodiscard]] double chebyshev(std::span<const double> coefficients, double s) noexcept;

/// The same series and its derivative df/ds, evaluated together by differentiating the Clenshaw
/// recurrence.
[[nodiscard]] ChebyshevValue chebyshev_with_derivative(std::span<const double> coefficients, double s) noexcept;

/// Evaluates the series on the interval [midpoint - radius, midpoint + radius] at `x`: s = (x - midpoint) /
/// radius, and the derivative is returned with respect to x (df/ds / radius). This is the form JPL SPK
/// type 2 and 3 records use, with x in TDB seconds past J2000 (NAIF SPK Required Reading).
/// Precondition: radius > 0.
[[nodiscard]] ChebyshevValue chebyshev_on_interval(std::span<const double> coefficients, double midpoint,
                                                   double radius, double x) noexcept;

} // namespace usim::math
