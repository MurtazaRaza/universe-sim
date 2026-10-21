#include "usim/result.hpp"

#include <catch2/catch_test_macros.hpp>

#include <set>
#include <string>
#include <string_view>

using usim::Error;
using usim::ErrorCode;
using usim::Result;

namespace {

Result<int> parse_digit(char c) {
    if (c < '0' || c > '9') {
        return usim::make_error(ErrorCode::parse_error, "not a digit");
    }
    return c - '0';
}

} // namespace

TEST_CASE("Result holds a value or an error", "[result]") {
    const Result<int> ok = parse_digit('7');
    REQUIRE(ok.has_value());
    CHECK(static_cast<bool>(ok));
    CHECK(ok.value() == 7);
    CHECK(ok.value_or(-1) == 7);

    const Result<int> bad = parse_digit('x');
    REQUIRE_FALSE(bad.has_value());
    CHECK(bad.error().code == ErrorCode::parse_error);
    CHECK(bad.error().message == "not a digit");
    CHECK(bad.value_or(-1) == -1);
}

TEST_CASE("Result moves its value and error out", "[result]") {
    Result<std::string> text = std::string("payload");
    const std::string moved = std::move(text).value();
    CHECK(moved == "payload");

    Result<std::string> failure = usim::make_error(ErrorCode::not_found, "missing");
    const Error error = std::move(failure).error();
    CHECK(error.code == ErrorCode::not_found);
}

TEST_CASE("Result<void> reports success or an error", "[result]") {
    const Result<void> ok;
    CHECK(ok.has_value());

    const Result<void> bad = usim::make_error(ErrorCode::io_error, "disk");
    REQUIRE_FALSE(bad.has_value());
    CHECK(bad.error().code == ErrorCode::io_error);
}

TEST_CASE("every error code has a unique snake_case name", "[result]") {
    std::set<std::string_view> names;
    for (std::int32_t value = 1; value <= usim::k_max_error_code; ++value) {
        const std::string_view name = usim::to_string(static_cast<ErrorCode>(value));
        CHECK(name != "unknown");
        CHECK(name.find_first_not_of("abcdefghijklmnopqrstuvwxyz_") == std::string_view::npos);
        names.insert(name);
    }
    CHECK(names.size() == static_cast<std::size_t>(usim::k_max_error_code));
    CHECK(usim::to_string(static_cast<ErrorCode>(usim::k_max_error_code + 1)) == "unknown");
}
