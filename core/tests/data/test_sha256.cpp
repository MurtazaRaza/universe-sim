#include "fixtures/test_pack_expected.hpp"
#include "usim/data/sha256.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace {

std::string hash_text(std::string_view text) {
    return usim::data::to_hex(usim::data::sha256(std::as_bytes(std::span(text.data(), text.size()))));
}

} // namespace

TEST_CASE("SHA-256 matches the FIPS 180-4 examples", "[data][sha256]") {
    // NIST FIPS 180-4 example messages (csrc.nist.gov, "Examples with Intermediate Values": SHA256.pdf and
    // SHA2_Additional.pdf).
    CHECK(hash_text("abc") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    CHECK(hash_text("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq") ==
          "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1");
    CHECK(hash_text(std::string(1000000, 'a')) == "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0");
}

TEST_CASE("SHA-256 matches Python hashlib at the padding boundaries", "[data][sha256]") {
    for (const auto& c : usim::test::pack::k_sha256_cases) {
        INFO("length " << c.length);
        CHECK(hash_text(std::string(c.length, 'a')) == c.sha256);
    }
}
