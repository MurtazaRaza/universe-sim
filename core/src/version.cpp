#include "usim/version.hpp"

#if !defined(USIM_VERSION_MAJOR) || !defined(USIM_VERSION_MINOR) || !defined(USIM_VERSION_PATCH) || \
    !defined(USIM_VERSION_STRING)
#error "The USIM_VERSION_* macros must be defined by the build system (see core/src/CMakeLists.txt)"
#endif

namespace usim {

Version version() noexcept {
    return Version{USIM_VERSION_MAJOR, USIM_VERSION_MINOR, USIM_VERSION_PATCH};
}

std::string_view version_string() noexcept {
    return USIM_VERSION_STRING;
}

} // namespace usim
