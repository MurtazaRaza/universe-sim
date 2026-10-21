#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <string>

namespace usim::data {

/// A SHA-256 digest: 32 bytes.
using Sha256Digest = std::array<std::uint8_t, 32>;

/// SHA-256 of `bytes` (NIST FIPS 180-4). Used to verify the content hash of a data pack.
[[nodiscard]] Sha256Digest sha256(std::span<const std::byte> bytes) noexcept;

/// Lowercase hexadecimal text of a digest (64 characters), as printed by sha256sum.
[[nodiscard]] std::string to_hex(std::span<const std::uint8_t> digest);

} // namespace usim::data
