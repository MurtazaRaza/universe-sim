cmake_minimum_required(VERSION 3.25)

project(UniverseSimCore
    VERSION 0.1.0
    DESCRIPTION "Engine-agnostic universe simulation core"
    LANGUAGES CXX)

# Tests and the CLI are built by default only when this is the top-level project,
# so the core can be embedded in other builds (for example an engine module) without them.
option(USIM_BUILD_TESTS "Build the core unit tests" ${PROJECT_IS_TOP_LEVEL})
option(USIM_BUILD_CLI "Build the usim command-line host" ${PROJECT_IS_TOP_LEVEL})
option(USIM_BUILD_SHARED_CAPI "Build the universesim shared library (C ABI) for engine hosts" ON)
option(USIM_WARNINGS_AS_ERRORS "Treat compiler warnings as errors" ON)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Only symbols marked USIM_API leave the shared library.
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

# Link the MSVC runtime statically so the engine plugin needs no redistributable.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake")
include(UsimCompilerOptions)

add_subdirectory(src)

if(USIM_BUILD_SHARED_CAPI)
    add_subdirectory(capi)
endif()

if(USIM_BUILD_CLI)
    add_subdirectory(apps/usim-cli)
endif()

if(USIM_BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()
