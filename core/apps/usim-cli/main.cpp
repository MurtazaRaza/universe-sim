// usim: headless host for the UniverseSim core.
//
// Phase 0 only reports the version. Queries (catalog, state, snapshot, orbit) are added in
// Phase 4 (IMPLEMENTATION_PLAN P4-06).

#include "usim/version.hpp"

#include <cstdio>
#include <span>
#include <string_view>

namespace {

constexpr std::string_view k_usage =
    "usim - UniverseSim headless host\n"
    "\n"
    "Usage:\n"
    "  usim --version   Print the core library version\n"
    "  usim --help      Show this help\n";

void print_usage(std::FILE* stream) {
    std::fwrite(k_usage.data(), 1, k_usage.size(), stream);
}

} // namespace

int main(int argc, char* argv[]) {
    const std::span<char*> args{argv, static_cast<std::size_t>(argc)};

    if (args.size() == 2) {
        const std::string_view command{args[1]};
        if (command == "--version") {
            const std::string_view version = usim::version_string();
            std::printf("usim %.*s\n", static_cast<int>(version.size()), version.data());
            return 0;
        }
        if (command == "--help" || command == "-h") {
            print_usage(stdout);
            return 0;
        }
    }

    if (args.size() <= 1) {
        print_usage(stdout);
        return 0;
    }

    print_usage(stderr);
    return 2;
}
