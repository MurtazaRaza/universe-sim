// usim: headless host for the UniverseSim core (SPEC FR-CLI-01). The commands are in cli.cpp.

#include "cli.hpp"

#include <iostream>
#include <span>
#include <string>
#include <vector>

int main(int argc, char* argv[]) {
    const std::span<char*> raw{argv, static_cast<std::size_t>(argc)};
    const std::vector<std::string> args(raw.begin() + (raw.empty() ? 0 : 1), raw.end());
    return usim::cli::run(args, std::cout, std::cerr);
}
