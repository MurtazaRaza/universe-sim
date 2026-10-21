/*
 * UniverseSim C ABI.
 *
 * This header is the only interface engine hosts (Unity, Godot, Unreal, ...) use to talk to the
 * core. Conventions (see docs/ARCHITECTURE.md, section 5):
 *   - Plain C types only: fixed-width integers, double, and structs of those. No bool, no enums
 *     in structs (use int32_t), no C++ types.
 *   - Every function is safe to call from C: no exception ever crosses this boundary.
 *   - Strings are UTF-8. Strings returned by the library are owned by it; their lifetime is
 *     documented per function. Callers never free them.
 *   - Output arrays are caller-owned buffers with an explicit capacity.
 *   - Any change that breaks binary compatibility increments USIM_ABI_VERSION. Hosts compare
 *     usim_abi_version() with the value they were written against before calling anything else.
 */
#ifndef USIM_USIM_H
#define USIM_USIM_H

#include <stdint.h>

#if defined(_WIN32)
#    if defined(USIM_CAPI_BUILD)
#        define USIM_API __declspec(dllexport)
#    else
#        define USIM_API __declspec(dllimport)
#    endif
#else
#    define USIM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Version of this binary interface. */
#define USIM_ABI_VERSION 1u

/* Returns the ABI version the library was built with (USIM_ABI_VERSION of its header). */
USIM_API uint32_t usim_abi_version(void);

/* Returns the core library version as "major.minor.patch".
 * The string has static storage duration and must not be freed. */
USIM_API const char* usim_version_string(void);

/* Status codes. Fallible functions return one of these as int32_t: 0 on success, otherwise the reason.
 * Each non-zero value equals the C++ usim::ErrorCode with the same name. Codes are never renumbered,
 * but new ones may be appended, so hosts must treat unknown non-zero values as generic failures. */
#define USIM_STATUS_OK 0
#define USIM_STATUS_INVALID_ARGUMENT 1     /* an argument is outside its domain */
#define USIM_STATUS_PARSE_ERROR 2          /* text could not be parsed */
#define USIM_STATUS_OUT_OF_RANGE 3         /* outside the range a model supports (e.g. UTC before 1972) */
#define USIM_STATUS_OUT_OF_COVERAGE 4      /* no data covers the requested instant */
#define USIM_STATUS_NOT_FOUND 5            /* unknown body, frame or source */
#define USIM_STATUS_UNSUPPORTED 6          /* valid, but not implemented by this version */
#define USIM_STATUS_DATA_FORMAT 7          /* malformed or inconsistent input data */
#define USIM_STATUS_INCOMPATIBLE_VERSION 8 /* input data format version not supported */
#define USIM_STATUS_IO_ERROR 9             /* a file could not be read */
#define USIM_STATUS_INTERNAL 10            /* a bug in the core */

/* Returns the lowercase name of a status code without the prefix ("ok", "invalid_argument", ...), or
 * "unknown" for an unrecognized value. The string has static storage duration and must not be freed. */
USIM_API const char* usim_status_name(int32_t status);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* USIM_USIM_H */
