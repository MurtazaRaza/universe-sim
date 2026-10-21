// Implementation of the C ABI declared in usim/usim.h.
//
// Rules for every function in this file: no exception may escape (catch and convert to a status
// once fallible functions exist), validate every pointer argument, and pass only C types.

#include "usim/usim.h"

#include "usim/version.hpp"

extern "C" {

USIM_API uint32_t usim_abi_version(void) {
    return USIM_ABI_VERSION;
}

USIM_API const char* usim_version_string(void) {
    // version_string() views a null-terminated literal with static storage duration.
    return usim::version_string().data();
}

} // extern "C"
