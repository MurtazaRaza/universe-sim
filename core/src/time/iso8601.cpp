#include "usim/time/iso8601.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstdio>

namespace usim::time {

namespace {

constexpr std::array<std::int64_t, 10> k_powers_of_ten{
    1, 10, 100, 1'000, 10'000, 100'000, 1'000'000, 10'000'000, 100'000'000, 1'000'000'000};

class Parser {
public:
    explicit Parser(std::string_view text) : text_(text) {}

    [[nodiscard]] bool at_end() const noexcept { return pos_ == text_.size(); }

    [[nodiscard]] bool peek(char c) const noexcept { return pos_ < text_.size() && text_[pos_] == c; }

    [[nodiscard]] bool peek_digit() const noexcept {
        return pos_ < text_.size() && text_[pos_] >= '0' && text_[pos_] <= '9';
    }

    bool accept(char c) noexcept {
        if (peek(c)) {
            ++pos_;
            return true;
        }
        return false;
    }

    // Reads between min_digits and max_digits decimal digits into `out`.
    bool digits(std::size_t min_digits, std::size_t max_digits, std::int64_t& out) noexcept {
        std::int64_t value = 0;
        std::size_t count = 0;
        while (count < max_digits && peek_digit()) {
            value = value * 10 + (text_[pos_] - '0');
            ++pos_;
            ++count;
        }
        out = value;
        return count >= min_digits;
    }

    [[nodiscard]] std::string_view rest() const noexcept { return text_.substr(pos_); }

    [[nodiscard]] Error error(std::string_view what) const {
        return make_error(ErrorCode::parse_error, std::string(what) + " at position " + std::to_string(pos_) +
                                                      " in \"" + std::string(text_) + "\"");
    }

    void skip(std::size_t n) noexcept { pos_ = std::min(pos_ + n, text_.size()); }

private:
    std::string_view text_;
    std::size_t pos_ = 0;
};

void append_padded(std::string& out, std::int64_t value, int width) {
    std::array<char, 32> buffer{};
    const int written = std::snprintf(buffer.data(), buffer.size(), "%0*lld", width, static_cast<long long>(value));
    out.append(buffer.data(), static_cast<std::size_t>(std::max(written, 0)));
}

} // namespace

Result<ScaledDateTime> parse_iso8601(std::string_view text) {
    Parser p(text);
    DateTime dt{{0, 1, 1}, 0, 0, 0.0};

    // Date.
    const bool negative = p.accept('-');
    if (!negative) {
        p.accept('+');
    }
    std::int64_t year = 0;
    if (!p.digits(4, 9, year)) {
        return p.error("expected a year of at least four digits");
    }
    if (p.peek_digit()) {
        return p.error("year has too many digits");
    }
    std::int64_t month = 0;
    std::int64_t day = 0;
    if (!p.accept('-') || !p.digits(2, 2, month)) {
        return p.error("expected '-MM' after the year");
    }
    if (!p.accept('-') || !p.digits(2, 2, day)) {
        return p.error("expected '-DD' after the month");
    }
    dt.date = {static_cast<std::int32_t>(negative ? -year : year), static_cast<std::int32_t>(month),
               static_cast<std::int32_t>(day)};
    if (month < 1 || month > 12) {
        return p.error("month must be 01 to 12");
    }
    if (day < 1 || day > 31) {
        return p.error("day must be 01 to 31");
    }

    // Optional time. A space is either the date-time separator or the start of the scale suffix.
    std::int64_t whole_seconds = 0;
    double fraction = 0.0;
    const bool has_time = p.peek('T') || (p.peek(' ') && p.rest().size() > 1 && p.rest()[1] >= '0' &&
                                          p.rest()[1] <= '9');
    if (has_time) {
        p.skip(1);
        std::int64_t hour = 0;
        std::int64_t minute = 0;
        if (!p.digits(2, 2, hour)) {
            return p.error("expected hh after the date");
        }
        if (!p.accept(':') || !p.digits(2, 2, minute)) {
            return p.error("expected ':mm' after the hour");
        }
        if (p.accept(':')) {
            if (!p.digits(2, 2, whole_seconds)) {
                return p.error("expected ss after the minutes");
            }
            if (p.accept('.')) {
                std::int64_t numerator = 0;
                std::int64_t denominator = 1;
                std::size_t count = 0;
                while (p.peek_digit()) {
                    std::int64_t digit = 0;
                    (void)p.digits(1, 1, digit);
                    if (count < 18) {
                        numerator = numerator * 10 + digit;
                        denominator *= 10;
                    }
                    ++count;
                }
                if (count == 0) {
                    return p.error("expected digits after the decimal point");
                }
                // Up to 15 digits both operands are exact doubles, so the division is correctly rounded.
                // Longer fractions round twice, far below the resolution of the result.
                fraction = static_cast<double>(numerator) / static_cast<double>(denominator);
            }
        }
        if (hour > 23) {
            return p.error("hour must be 00 to 23");
        }
        if (minute > 59) {
            return p.error("minute must be 00 to 59");
        }
        dt.hour = static_cast<std::int32_t>(hour);
        dt.minute = static_cast<std::int32_t>(minute);
    }

    // Scale suffix.
    TimeScale scale = TimeScale::utc;
    if (has_time && p.accept('Z')) {
        scale = TimeScale::utc;
    } else if (p.accept(' ')) {
        const std::string_view name = p.rest();
        constexpr std::array<TimeScale, 4> k_scales{TimeScale::tdb, TimeScale::tt, TimeScale::tai, TimeScale::utc};
        const auto* match = std::find_if(k_scales.begin(), k_scales.end(),
                                         [&](TimeScale s) { return name == to_string(s); });
        if (match == k_scales.end()) {
            return p.error("expected a time scale (TDB, TT, TAI or UTC)");
        }
        scale = *match;
        p.skip(name.size());
    } else {
        return p.error("expected a time scale suffix (' TDB', ' TT', ' TAI', ' UTC' or 'Z')");
    }
    if (!p.at_end()) {
        return p.error("unexpected text after the time scale");
    }

    const bool leap_second = whole_seconds == 60 && scale == TimeScale::utc && dt.hour == 23 && dt.minute == 59;
    if (whole_seconds > 59 && !leap_second) {
        return p.error("second must be 00 to 59 (60 only for a UTC leap second at 23:59)");
    }
    dt.second = static_cast<double>(whole_seconds) + fraction;
    return ScaledDateTime{dt, scale};
}

std::string format_iso8601(const DateTime& date_time, TimeScale scale, int fractional_digits) {
    const int digits = std::clamp(fractional_digits, 0, 9);
    const std::int64_t unit = k_powers_of_ten[static_cast<std::size_t>(digits)];

    // Round the seconds to the requested digits, but never into the next minute.
    const std::int64_t seconds_in_minute = date_time.second >= 60.0 ? 61 : 60;
    std::int64_t units = std::llround(date_time.second * static_cast<double>(unit));
    units = std::clamp<std::int64_t>(units, 0, seconds_in_minute * unit - 1);

    std::string out;
    out.reserve(40);
    const std::int64_t year = date_time.date.year;
    if (year < 0) {
        out += '-';
    } else if (year > 9999) {
        out += '+';
    }
    append_padded(out, year < 0 ? -year : year, 4);
    out += '-';
    append_padded(out, date_time.date.month, 2);
    out += '-';
    append_padded(out, date_time.date.day, 2);
    out += 'T';
    append_padded(out, date_time.hour, 2);
    out += ':';
    append_padded(out, date_time.minute, 2);
    out += ':';
    append_padded(out, units / unit, 2);
    if (digits > 0) {
        out += '.';
        append_padded(out, units % unit, digits);
    }
    out += ' ';
    out += to_string(scale);
    return out;
}

} // namespace usim::time
