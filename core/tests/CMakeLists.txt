# Unit tests (Catch2, pinned by archive hash).

include(FetchContent)

FetchContent_Declare(Catch2
    URL https://github.com/catchorg/Catch2/archive/refs/tags/v3.16.0.tar.gz
    URL_HASH SHA256=0957cae5821b17ce07f0833aaa52b5137643a8382203221f363a8303c109af34
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE
    SYSTEM)
FetchContent_MakeAvailable(Catch2)

add_executable(usim_core_tests
    catalog/test_catalog.cpp
    frames/test_frames.cpp
    math/test_chebyshev.cpp
    math/test_quat.cpp
    math/test_vec_mat.cpp
    presentation/test_axis_convention.cpp
    test_constants.cpp
    test_result.cpp
    test_version.cpp
    time/test_calendar.cpp
    time/test_iso8601.cpp
    time/test_time_point.cpp
    time/test_time_scales.cpp)

target_link_libraries(usim_core_tests PRIVATE usim::core Catch2::Catch2WithMain)

# Tests include shared helpers (test_support.hpp) and generated reference data (fixtures/) by path.
target_include_directories(usim_core_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

# The C ABI tests link the shared library, so they exercise the exported symbols.
if(TARGET usim::capi)
    target_sources(usim_core_tests PRIVATE test_capi.cpp)
    target_link_libraries(usim_core_tests PRIVATE usim::capi)
endif()

usim_set_compiler_options(usim_core_tests)

list(APPEND CMAKE_MODULE_PATH "${catch2_SOURCE_DIR}/extras")
include(Catch)
catch_discover_tests(usim_core_tests DISCOVERY_MODE PRE_TEST)
