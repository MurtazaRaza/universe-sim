#pragma once

#include <ostream>
#include <span>
#include <string>

namespace usim::cli {

/// Exit codes of the usim command.
inline constexpr int k_exit_ok = 0;     ///< Success.
inline constexpr int k_exit_failed = 1; ///< The query failed (the message names the reason and status).
inline constexpr int k_exit_usage = 2;  ///< The command line is invalid.

/// Runs the usim command line (SPEC FR-CLI-01) with `args` (without the program name), writing results to
/// `out` and messages to `err`. Returns the exit code.
int run(std::span<const std::string> args, std::ostream& out, std::ostream& err);

} // namespace usim::cli
