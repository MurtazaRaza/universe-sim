# Compiler settings shared by every UniverseSim target.
#
# Floating-point rules support determinism (SPEC NFR-04): no fast-math and no FMA contraction,
# so results do not depend on whether the compiler fuses multiply-add instructions.
# Apple Clang contracts by default on arm64, which is why -ffp-contract=off is set explicitly.

function(usim_set_compiler_options target)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /permissive- /utf-8 /fp:precise)
        if(USIM_WARNINGS_AS_ERRORS)
            target_compile_options(${target} PRIVATE /WX)
        endif()
    else()
        target_compile_options(${target} PRIVATE
            -Wall
            -Wextra
            -Wpedantic
            -Wshadow
            -Wconversion
            -Wsign-conversion
            -Wdouble-promotion
            -Wold-style-cast
            -Wnon-virtual-dtor
            -Wcast-align
            -Wnull-dereference
            -ffp-contract=off)
        if(USIM_WARNINGS_AS_ERRORS)
            target_compile_options(${target} PRIVATE -Werror)
        endif()
    endif()
endfunction()
