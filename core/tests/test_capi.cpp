#include "usim/usim.h"
#include "usim/version.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string_view>

TEST_CASE("C ABI reports the ABI version of its header", "[capi]") {
    CHECK(usim_abi_version() == USIM_ABI_VERSION);
}

TEST_CASE("C ABI version string matches the core", "[capi]") {
    const char* version = usim_version_string();

    REQUIRE(version != nullptr);
    CHECK(std::string_view{version} == usim::version_string());
}
