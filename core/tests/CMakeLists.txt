# Unit tests (Catch2, pinned by archive hash).

include(FetchContent)

FetchContent_Declare(Catch2
    URL https://github.com/catchorg/Catch2/archive/refs/tags/v3.16.0.tar.gz
    URL_HASH SHA256=0957cae5821b17ce07f0833aaa52b5137643a8382203221f363a8303c109af34
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE
    SYSTEM)
FetchContent_MakeAvailable(Catch2)

add_executable(usim_core_tests
    test_version.cpp)

target_link_libraries(usim_core_tests PRIVATE usim::core Catch2::Catch2WithMain)

# The C ABI tests link the shared library, so they exercise the exported symbols.
if(TARGET usim::capi)
    target_sources(usim_core_tests PRIVATE test_capi.cpp)
    target_link_libraries(usim_core_tests PRIVATE usim::capi)
endif()

usim_set_compiler_options(usim_core_tests)

list(APPEND CMAKE_MODULE_PATH "${catch2_SOURCE_DIR}/extras")
include(Catch)
catch_discover_tests(usim_core_tests DISCOVERY_MODE PRE_TEST)
