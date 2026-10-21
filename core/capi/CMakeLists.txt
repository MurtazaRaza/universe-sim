# universesim: shared library exposing the C ABI to engine hosts.
#
# The output has no "lib" prefix on any platform (universesim.dylib, universesim.dll,
# universesim.so) so hosts can load it by the same name everywhere, for example
# [DllImport("universesim")] in C#. It carries no VERSION/SOVERSION symlinks: compatibility is
# checked at runtime through usim_abi_version().

add_library(universesim SHARED
    src/usim_capi.cpp)
add_library(usim::capi ALIAS universesim)

target_include_directories(universesim PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>)

target_link_libraries(universesim PRIVATE usim::core)
target_compile_definitions(universesim PRIVATE USIM_CAPI_BUILD)

set_target_properties(universesim PROPERTIES PREFIX "")

usim_set_compiler_options(universesim)
