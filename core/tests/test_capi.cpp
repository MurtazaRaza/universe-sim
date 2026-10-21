#include "usim/result.hpp"
#include "usim/usim.h"
#include "usim/version.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <string_view>

TEST_CASE("C ABI reports the ABI version of its header", "[capi]") {
    CHECK(usim_abi_version() == USIM_ABI_VERSION);
}

TEST_CASE("C ABI version string matches the core", "[capi]") {
    const char* version = usim_version_string();

    REQUIRE(version != nullptr);
    CHECK(std::string_view{version} == usim::version_string());
}

TEST_CASE("C ABI status names match the core error codes", "[capi]") {
    CHECK(std::string_view{usim_status_name(USIM_STATUS_OK)} == "ok");
    for (std::int32_t status = 1; status <= usim::k_max_error_code; ++status) {
        CHECK(std::string_view{usim_status_name(status)} == usim::to_string(static_cast<usim::ErrorCode>(status)));
    }
    CHECK(std::string_view{usim_status_name(-1)} == "unknown");
    CHECK(std::string_view{usim_status_name(usim::k_max_error_code + 1)} == "unknown");
    CHECK(std::string_view{usim_status_name(USIM_STATUS_OUT_OF_COVERAGE)} == "out_of_coverage");
}
