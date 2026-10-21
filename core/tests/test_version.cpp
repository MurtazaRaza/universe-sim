#include "usim/version.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

TEST_CASE("version string matches the version components", "[version]") {
    const usim::Version v = usim::version();
    const std::string expected =
        std::to_string(v.major) + "." + std::to_string(v.minor) + "." + std::to_string(v.patch);

    CHECK(usim::version_string() == expected);
}

TEST_CASE("version string is null-terminated", "[version]") {
    const std::string_view s = usim::version_string();

    REQUIRE_FALSE(s.empty());
    CHECK(s.data()[s.size()] == '\0');
}
