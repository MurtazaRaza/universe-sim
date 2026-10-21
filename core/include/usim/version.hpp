#pragma once

#include <string_view>

namespace usim {

/// Semantic version of the core library.
struct Version {
    int major;
    int minor;
    int patch;
};

/// Version of the core library this binary was built from.
[[nodiscard]] Version version() noexcept;

/// The same version formatted as "major.minor.patch".
/// The view refers to a null-terminated string with static storage duration.
[[nodiscard]] std::string_view version_string() noexcept;

} // namespace usim
