#include "usim/result.hpp"

namespace usim {

std::string_view to_string(ErrorCode code) noexcept {
    switch (code) {
    case ErrorCode::invalid_argument:
        return "invalid_argument";
    case ErrorCode::parse_error:
        return "parse_error";
    case ErrorCode::out_of_range:
        return "out_of_range";
    case ErrorCode::out_of_coverage:
        return "out_of_coverage";
    case ErrorCode::not_found:
        return "not_found";
    case ErrorCode::unsupported:
        return "unsupported";
    case ErrorCode::data_format:
        return "data_format";
    case ErrorCode::incompatible_version:
        return "incompatible_version";
    case ErrorCode::io_error:
        return "io_error";
    case ErrorCode::internal:
        return "internal";
    }
    return "unknown";
}

} // namespace usim
