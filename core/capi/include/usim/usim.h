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

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* USIM_USIM_H */
