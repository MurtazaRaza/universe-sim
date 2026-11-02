# Compiler settings shared by every UniverseSim target.
#
# Floating-point rules support determinism (SPEC NFR-04): no fast-math and no FMA contraction,
# so results do not depend on whether the compiler fuses multiply-add instructions.
# Apple Clang contracts by default on arm64, which is why -ffp-contract=off is set explicitly.

function(usim_set_compiler_options target)
    if(MSVC)
        # /fp:precise generates no FMA contractions from MSVC 19.30 (VS 2022 17.0) on; earlier versions could, and
        # only /fp:contract turns them back on (Microsoft C++ Team Blog, "The /fp:contract flag and changes to FP
        # modes in VS2022"). Older compilers are rejected below.
        if(MSVC_VERSION LESS 1930)
            message(FATAL_ERROR "MSVC 19.30 (Visual Studio 2022) or newer is required: older versions contract "
                                "multiply-adds under /fp:precise, which breaks determinism (SPEC NFR-04).")
        endif()
        target_compile_options(${target} PRIVATE /W4 /permissive- /utf-8 /fp:precise)
        # MSVC deprecates standard C functions such as std::getenv (C4996) in favor of its own non-portable
        # "_s" variants. The core uses the standard functions, so the deprecation is turned off rather than
        # the warning level lowered.
        target_compile_definitions(${target} PRIVATE _CRT_SECURE_NO_WARNINGS)
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
