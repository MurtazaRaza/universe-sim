#pragma once

#include "usim/math/mat3.hpp"
#include "usim/math/vec3.hpp"

namespace usim::frames {

/// Position and velocity, in km and km/s.
struct StateVector {
    math::Vec3 position_km;    ///< Position, km.
    math::Vec3 velocity_km_s;  ///< Velocity, km/s.
};

/// A transformation of states from one frame to another at one instant, like a SPICE sxform matrix:
///
///     position' = R position
///     velocity' = R velocity + dR/dt position
///
/// where R is the rotation from the source frame to the target frame and dR/dt its time derivative
/// (zero between inertial frames).
class FrameTransform {
public:
    /// The identity transform.
    constexpr FrameTransform() noexcept = default;

    /// A transform with rotation `rotation` and rotation rate `rotation_rate_per_s` (dR/dt, per second).
    constexpr FrameTransform(const math::Mat3& rotation, const math::Mat3& rotation_rate_per_s) noexcept
        : rotation_(rotation), rate_(rotation_rate_per_s) {}

    /// A transform between two inertial frames (rotation rate zero).
    [[nodiscard]] static constexpr FrameTransform inertial(const math::Mat3& rotation) noexcept {
        return {rotation, math::Mat3{}};
    }

    /// The rotation R.
    [[nodiscard]] constexpr const math::Mat3& rotation() const noexcept { return rotation_; }

    /// The rotation rate dR/dt, per second.
    [[nodiscard]] constexpr const math::Mat3& rotation_rate_per_s() const noexcept { return rate_; }

    /// Transforms a position (or any direction, which ignores the rotation rate).
    [[nodiscard]] constexpr math::Vec3 apply(const math::Vec3& position) const noexcept {
        return rotation_ * position;
    }

    /// Transforms a state, including the velocity term from the rotation rate.
    [[nodiscard]] constexpr StateVector apply(const StateVector& state) const noexcept {
        return {rotation_ * state.position_km, rotation_ * state.velocity_km_s + rate_ * state.position_km};
    }

    /// The transform that applies this one first and then `next` (from this one's source frame to `next`'s
    /// target frame).
    [[nodiscard]] constexpr FrameTransform then(const FrameTransform& next) const noexcept {
        return {next.rotation_ * rotation_, next.rate_ * rotation_ + next.rotation_ * rate_};
    }

    /// The reverse transform. For an orthonormal R, the inverse of (R, dR/dt) is (R^T, (dR/dt)^T).
    [[nodiscard]] constexpr FrameTransform inverse() const noexcept {
        return {math::transpose(rotation_), math::transpose(rate_)};
    }

private:
    math::Mat3 rotation_ = math::Mat3::identity();
    math::Mat3 rate_{};
};

} // namespace usim::frames
