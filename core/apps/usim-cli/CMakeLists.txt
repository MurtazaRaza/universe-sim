# usim: headless command-line host (SPEC FR-CLI-01). Links the core statically, no engine.

add_executable(usim
    main.cpp)

target_link_libraries(usim PRIVATE usim::core)

usim_set_compiler_options(usim)
