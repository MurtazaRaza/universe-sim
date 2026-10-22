# usim: headless command-line host (SPEC FR-CLI-01). Links the core statically, no engine.
# The commands live in a small library so the tests can run them without a process.

add_library(usim_cli_lib STATIC
    cli.cpp)
add_library(usim::cli ALIAS usim_cli_lib)
target_include_directories(usim_cli_lib PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
target_link_libraries(usim_cli_lib PUBLIC usim::core)
usim_set_compiler_options(usim_cli_lib)

add_executable(usim
    main.cpp)

target_link_libraries(usim PRIVATE usim::cli)

usim_set_compiler_options(usim)
