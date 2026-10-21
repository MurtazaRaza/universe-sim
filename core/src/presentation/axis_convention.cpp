#include "usim/presentation/axis_convention.hpp"

namespace usim::presentation {

// The component formulas below are the closed forms of S v and (w, det(S) S u). The unit tests check them
// against the matrix definitions for random rotations.

std::string_view to_string(AxisConvention convention) noexcept {
    switch (convention) {
    case AxisConvention::core:
        return "core";
    case AxisConvention::unity:
        return "unity";
    case AxisConvention::godot:
        return "godot";
    case AxisConvention::unreal:
        return "unreal";
    }
    return "unknown";
}

math::Mat3 axis_matrix(AxisConvention convention) noexcept {
    switch (convention) {
    case AxisConvention::core:
        return math::Mat3::identity();
    case AxisConvention::unity:
        return math::Mat3{{1, 0, 0, 0, 0, 1, 0, 1, 0}};
    case AxisConvention::godot:
        return math::Mat3{{1, 0, 0, 0, 0, 1, 0, -1, 0}};
    case AxisConvention::unreal:
        return math::Mat3{{1, 0, 0, 0, -1, 0, 0, 0, 1}};
    }
    return math::Mat3::identity();
}

bool is_left_handed(AxisConvention convention) noexcept {
    return convention == AxisConvention::unity || convention == AxisConvention::unreal;
}

math::Vec3 to_host(AxisConvention convention, const math::Vec3& v) noexcept {
    switch (convention) {
    case AxisConvention::core:
        return v;
    case AxisConvention::unity:
        return {v.x, v.z, v.y};
    case AxisConvention::godot:
        return {v.x, v.z, -v.y};
    case AxisConvention::unreal:
        return {v.x, -v.y, v.z};
    }
    return v;
}

math::Quat to_host(AxisConvention convention, const math::Quat& q) noexcept {
    switch (convention) {
    case AxisConvention::core:
        return q;
    case AxisConvention::unity:
        return {q.w, -q.x, -q.z, -q.y};
    case AxisConvention::godot:
        return {q.w, q.x, q.z, -q.y};
    case AxisConvention::unreal:
        return {q.w, -q.x, q.y, -q.z};
    }
    return q;
}

math::Vec3 from_host(AxisConvention convention, const math::Vec3& v) noexcept {
    switch (convention) {
    case AxisConvention::core:
    case AxisConvention::unity:  // its own inverse
    case AxisConvention::unreal: // its own inverse
        return to_host(convention, v);
    case AxisConvention::godot:
        return {v.x, -v.z, v.y};
    }
    return v;
}

math::Quat from_host(AxisConvention convention, const math::Quat& q) noexcept {
    switch (convention) {
    case AxisConvention::core:
    case AxisConvention::unity:  // its own inverse
    case AxisConvention::unreal: // its own inverse
        return to_host(convention, q);
    case AxisConvention::godot:
        return {q.w, q.x, -q.z, q.y};
    }
    return q;
}

} // namespace usim::presentation
