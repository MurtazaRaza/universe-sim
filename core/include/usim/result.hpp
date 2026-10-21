#pragma once

#include <cstdint>
#include <cstdlib>
#include <string>
#include <string_view>
#include <type_traits>
#include <utility>
#include <variant>

namespace usim {

/// Reasons an operation can fail. Each value equals the C ABI status code with the same name
/// (`USIM_STATUS_<NAME>` in usim.h); success is status 0 and has no ErrorCode.
/// Values are stable: never renumber or reuse them, only append.
enum class ErrorCode : std::int32_t {
    invalid_argument = 1,     ///< An argument is outside its domain (for example a non-existent date).
    parse_error = 2,          ///< Text could not be parsed.
    out_of_range = 3,         ///< A value lies outside the range a model supports (for example UTC before 1972).
    out_of_coverage = 4,      ///< No data covers the requested instant (SPEC FR-SIM-03).
    not_found = 5,            ///< An identifier (body, frame, source) is unknown.
    unsupported = 6,          ///< The request is valid but not implemented by this version of the core.
    data_format = 7,          ///< Input data (for example a data pack) is malformed or inconsistent.
    incompatible_version = 8, ///< Input data uses a format version this core cannot read.
    io_error = 9,             ///< A file could not be read.
    internal = 10,            ///< A bug in the core: an invariant was violated or an exception escaped.
};

/// The highest ErrorCode value. Used by tests that walk every code.
inline constexpr std::int32_t k_max_error_code = static_cast<std::int32_t>(ErrorCode::internal);

/// Snake-case name of the code, identical to the C ABI name without the `USIM_STATUS_` prefix
/// (lowercase). Returns "unknown" for values outside the enumeration.
[[nodiscard]] std::string_view to_string(ErrorCode code) noexcept;

/// An error: a machine-readable code and a human-readable message (English, no trailing period).
struct Error {
    ErrorCode code;
    std::string message;
};

namespace detail {

/// Called when a Result is accessed in the wrong state. This is a programming error, so it terminates
/// instead of returning a value that does not exist.
[[noreturn]] inline void bad_result_access() noexcept {
    std::abort();
}

} // namespace detail

/// Either a value of type T or an Error. Fallible core operations return this instead of throwing.
///
/// Accessing value() on an error, or error() on a value, is a programming error and terminates the
/// process. Check has_value() (or the bool conversion) first.
template <typename T>
class [[nodiscard]] Result {
    static_assert(!std::is_same_v<std::remove_cv_t<T>, Error>, "Result<Error> is ambiguous");
    static_assert(!std::is_reference_v<T>, "Result does not hold references; use a pointer");

public:
    /// A successful result holding `value`.
    Result(const T& value) : storage_(std::in_place_index<0>, value) {}

    /// A successful result holding `value`.
    Result(T&& value) : storage_(std::in_place_index<0>, std::move(value)) {}

    /// A failed result.
    Result(Error error) : storage_(std::in_place_index<1>, std::move(error)) {}

    /// True if this result holds a value.
    [[nodiscard]] bool has_value() const noexcept { return storage_.index() == 0; }

    /// Same as has_value().
    [[nodiscard]] explicit operator bool() const noexcept { return has_value(); }

    /// The value. Precondition: has_value().
    [[nodiscard]] T& value() & {
        if (!has_value()) {
            detail::bad_result_access();
        }
        return *std::get_if<0>(&storage_);
    }

    /// The value. Precondition: has_value().
    [[nodiscard]] const T& value() const& {
        if (!has_value()) {
            detail::bad_result_access();
        }
        return *std::get_if<0>(&storage_);
    }

    /// The value, moved out. Precondition: has_value().
    [[nodiscard]] T&& value() && {
        if (!has_value()) {
            detail::bad_result_access();
        }
        return std::move(*std::get_if<0>(&storage_));
    }

    /// The value, or `fallback` if this result holds an error.
    template <typename U>
    [[nodiscard]] T value_or(U&& fallback) const& {
        return has_value() ? *std::get_if<0>(&storage_) : static_cast<T>(std::forward<U>(fallback));
    }

    /// The error. Precondition: !has_value().
    [[nodiscard]] const Error& error() const& {
        if (has_value()) {
            detail::bad_result_access();
        }
        return *std::get_if<1>(&storage_);
    }

    /// The error, moved out. Precondition: !has_value().
    [[nodiscard]] Error&& error() && {
        if (has_value()) {
            detail::bad_result_access();
        }
        return std::move(*std::get_if<1>(&storage_));
    }

private:
    std::variant<T, Error> storage_;
};

/// Result of an operation that produces no value: success, or an Error.
template <>
class [[nodiscard]] Result<void> {
public:
    /// A successful result.
    Result() noexcept = default;

    /// A failed result.
    Result(Error error) : error_(std::move(error)), has_error_(true) {}

    /// True on success.
    [[nodiscard]] bool has_value() const noexcept { return !has_error_; }

    /// Same as has_value().
    [[nodiscard]] explicit operator bool() const noexcept { return has_value(); }

    /// The error. Precondition: !has_value().
    [[nodiscard]] const Error& error() const& {
        if (!has_error_) {
            detail::bad_result_access();
        }
        return error_;
    }

private:
    Error error_{ErrorCode::internal, {}};
    bool has_error_ = false;
};

/// Convenience for building a failed Result: `return make_error(ErrorCode::not_found, "...");`
[[nodiscard]] inline Error make_error(ErrorCode code, std::string message) {
    return Error{code, std::move(message)};
}

} // namespace usim
