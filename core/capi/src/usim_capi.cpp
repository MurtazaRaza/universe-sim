// Implementation of the C ABI declared in usim/usim.h.
//
// Rules for every function in this file: no exception may escape (catch and convert to a status
// once fallible functions exist), validate every pointer argument, and pass only C types.

#include "usim/usim.h"

#include "usim/result.hpp"
#include "usim/version.hpp"

#include <cstdint>

// The status codes are the ErrorCode values (P1-08). Keep these in step with both enumerations.
static_assert(USIM_STATUS_INVALID_ARGUMENT == static_cast<std::int32_t>(usim::ErrorCode::invalid_argument));
static_assert(USIM_STATUS_PARSE_ERROR == static_cast<std::int32_t>(usim::ErrorCode::parse_error));
static_assert(USIM_STATUS_OUT_OF_RANGE == static_cast<std::int32_t>(usim::ErrorCode::out_of_range));
static_assert(USIM_STATUS_OUT_OF_COVERAGE == static_cast<std::int32_t>(usim::ErrorCode::out_of_coverage));
static_assert(USIM_STATUS_NOT_FOUND == static_cast<std::int32_t>(usim::ErrorCode::not_found));
static_assert(USIM_STATUS_UNSUPPORTED == static_cast<std::int32_t>(usim::ErrorCode::unsupported));
static_assert(USIM_STATUS_DATA_FORMAT == static_cast<std::int32_t>(usim::ErrorCode::data_format));
static_assert(USIM_STATUS_INCOMPATIBLE_VERSION ==
              static_cast<std::int32_t>(usim::ErrorCode::incompatible_version));
static_assert(USIM_STATUS_IO_ERROR == static_cast<std::int32_t>(usim::ErrorCode::io_error));
static_assert(USIM_STATUS_INTERNAL == static_cast<std::int32_t>(usim::ErrorCode::internal));
static_assert(USIM_STATUS_INTERNAL == usim::k_max_error_code, "a new ErrorCode needs a USIM_STATUS_ code");

extern "C" {

USIM_API uint32_t usim_abi_version(void) {
    return USIM_ABI_VERSION;
}

USIM_API const char* usim_version_string(void) {
    // version_string() views a null-terminated literal with static storage duration.
    return usim::version_string().data();
}

USIM_API const char* usim_status_name(int32_t status) {
    if (status == USIM_STATUS_OK) {
        return "ok";
    }
    if (status < 1 || status > usim::k_max_error_code) {
        return "unknown";
    }
    // to_string() views string literals, which are null-terminated with static storage duration.
    return usim::to_string(static_cast<usim::ErrorCode>(status)).data();
}

} // extern "C"
