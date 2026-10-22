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
    catalog/test_sky.cpp
    data/test_data_pack.cpp
    data/test_sha256.cpp
    ephemeris/test_chebyshev_segment.cpp
    ephemeris/test_coverage.cpp
    ephemeris/test_ephemeris_chain.cpp
    frames/test_frames.cpp
    frames/test_iau_rotation.cpp
    math/test_chebyshev.cpp
    math/test_quat.cpp
    math/test_vec_mat.cpp
    presentation/test_axis_convention.cpp
    presentation/test_view.cpp
    sim/test_orbit_path.cpp
    sim/test_simulation.cpp
    sim/test_simulation_clock.cpp
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
target_compile_definitions(usim_core_tests PRIVATE USIM_TEST_FIXTURES_DIR="${CMAKE_CURRENT_SOURCE_DIR}/fixtures")

# The C ABI tests link the shared library, so they exercise the exported symbols.
if(TARGET usim::capi)
    target_sources(usim_core_tests PRIVATE test_capi.cpp)
    target_link_libraries(usim_core_tests PRIVATE usim::capi)
endif()

usim_set_compiler_options(usim_core_tests)

# Validation tests (SPEC section 9): the core against independent reference data in data/reference/, using
# the default pack in data/packs/. Labeled `validation`; they skip (exit code 4) when the pack is absent.
set(USIM_DATA_DIR "${PROJECT_SOURCE_DIR}/../data" CACHE PATH "Repository data directory (packs and reference data)")

add_executable(usim_validation_tests
    validation/test_enlarged_bodies.cpp
    validation/test_orbit_paths.cpp
    validation/test_pack_kernel_values.cpp
    validation/test_val01_spk.cpp
    validation/test_val02_horizons.cpp
    validation/test_val03_time.cpp
    validation/test_val04_orientation.cpp)

target_link_libraries(usim_validation_tests PRIVATE usim::core Catch2::Catch2WithMain)
if(TARGET usim::capi)
    target_sources(usim_validation_tests PRIVATE validation/test_capi_default_pack.cpp)
    target_link_libraries(usim_validation_tests PRIVATE usim::capi)
endif()
target_include_directories(usim_validation_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_definitions(usim_validation_tests PRIVATE USIM_DATA_DIR="${USIM_DATA_DIR}")
usim_set_compiler_options(usim_validation_tests)

# The CLI commands run in-process through their library (P4-07), in their own executable so the core is
# linked once.
if(TARGET usim::cli)
    add_executable(usim_cli_tests cli/test_cli.cpp)
    target_link_libraries(usim_cli_tests PRIVATE usim::cli Catch2::Catch2WithMain)
    target_include_directories(usim_cli_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
    target_compile_definitions(usim_cli_tests PRIVATE USIM_TEST_FIXTURES_DIR="${CMAKE_CURRENT_SOURCE_DIR}/fixtures")
    usim_set_compiler_options(usim_cli_tests)
endif()

# The per-frame path must not allocate (SPEC NFR-03). This executable replaces the global operator new to
# count allocations, so it is kept apart from the other tests.
add_executable(usim_allocation_tests perf/test_no_allocation.cpp)
target_link_libraries(usim_allocation_tests PRIVATE usim::core Catch2::Catch2WithMain)
target_include_directories(usim_allocation_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_definitions(usim_allocation_tests PRIVATE
    USIM_TEST_FIXTURES_DIR="${CMAKE_CURRENT_SOURCE_DIR}/fixtures"
    USIM_DATA_DIR="${USIM_DATA_DIR}")
usim_set_compiler_options(usim_allocation_tests)

# Benchmarks (Catch2 BENCHMARK) on the default pack. Built by every preset, run by ctest only in release
# builds, where timings mean something (label `benchmark`).
add_executable(usim_benchmarks perf/bench_snapshot.cpp)
target_link_libraries(usim_benchmarks PRIVATE usim::core Catch2::Catch2WithMain)
target_include_directories(usim_benchmarks PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_definitions(usim_benchmarks PRIVATE USIM_DATA_DIR="${USIM_DATA_DIR}")
usim_set_compiler_options(usim_benchmarks)

list(APPEND CMAKE_MODULE_PATH "${catch2_SOURCE_DIR}/extras")
include(Catch)
catch_discover_tests(usim_core_tests DISCOVERY_MODE PRE_TEST)
catch_discover_tests(usim_validation_tests DISCOVERY_MODE PRE_TEST
    PROPERTIES LABELS validation SKIP_RETURN_CODE 4)
catch_discover_tests(usim_allocation_tests DISCOVERY_MODE PRE_TEST PROPERTIES SKIP_RETURN_CODE 4)
if(TARGET usim_cli_tests)
    catch_discover_tests(usim_cli_tests DISCOVERY_MODE PRE_TEST)
endif()
if(CMAKE_BUILD_TYPE STREQUAL "Release")
    catch_discover_tests(usim_benchmarks DISCOVERY_MODE PRE_TEST
        PROPERTIES LABELS benchmark SKIP_RETURN_CODE 4)
endif()
