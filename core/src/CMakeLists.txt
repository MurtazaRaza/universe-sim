# usim_core: the engine-agnostic simulation library (C++20, standard library only).

add_library(usim_core STATIC
    catalog/catalog.cpp
    catalog/sky.cpp
    data/data_pack.cpp
    data/sha256.cpp
    ephemeris/chebyshev_segment.cpp
    ephemeris/coverage.cpp
    ephemeris/ephemeris_chain.cpp
    frames/frames.cpp
    frames/iau_rotation.cpp
    math/chebyshev.cpp
    math/quat.cpp
    presentation/axis_convention.cpp
    presentation/view.cpp
    result.cpp
    sim/orbit_path.cpp
    sim/simulation.cpp
    sim/simulation_clock.cpp
    time/calendar.cpp
    time/iso8601.cpp
    time/time_scales.cpp
    version.cpp)
add_library(usim::core ALIAS usim_core)

target_include_directories(usim_core PUBLIC
    $<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/include>)

target_compile_features(usim_core PUBLIC cxx_std_20)

target_compile_definitions(usim_core PRIVATE
    USIM_VERSION_MAJOR=${PROJECT_VERSION_MAJOR}
    USIM_VERSION_MINOR=${PROJECT_VERSION_MINOR}
    USIM_VERSION_PATCH=${PROJECT_VERSION_PATCH}
    USIM_VERSION_STRING="${PROJECT_VERSION}")

# Linked into the shared C ABI library, so it must be position independent.
set_target_properties(usim_core PROPERTIES POSITION_INDEPENDENT_CODE ON)

usim_set_compiler_options(usim_core)
