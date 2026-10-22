#include "usim/ephemeris/coverage.hpp"

#include <algorithm>

namespace usim::ephemeris {

Coverage Coverage::of(std::span<const TimeInterval> intervals) {
    std::vector<TimeInterval> sorted;
    sorted.reserve(intervals.size());
    for (const TimeInterval& interval : intervals) {
        if (interval.start.is_valid() && interval.end.is_valid() && interval.start <= interval.end) {
            sorted.push_back(interval);
        }
    }
    std::sort(sorted.begin(), sorted.end(),
              [](const TimeInterval& a, const TimeInterval& b) { return a.start < b.start; });
    Coverage result;
    for (const TimeInterval& interval : sorted) {
        if (!result.intervals_.empty() && interval.start <= result.intervals_.back().end) {
            if (result.intervals_.back().end < interval.end) {
                result.intervals_.back().end = interval.end;
            }
        } else {
            result.intervals_.push_back(interval);
        }
    }
    return result;
}

bool Coverage::contains(time::Instant t) const noexcept {
    // The first interval that ends at or after t is the only one that can contain it.
    const auto it = std::lower_bound(
        intervals_.begin(), intervals_.end(), t,
        [](const TimeInterval& interval, const time::Instant& x) { return interval.end < x; });
    return it != intervals_.end() && it->contains(t);
}

std::optional<TimeInterval> Coverage::span() const noexcept {
    if (intervals_.empty()) {
        return std::nullopt;
    }
    return TimeInterval{intervals_.front().start, intervals_.back().end};
}

Coverage Coverage::intersection(const Coverage& other) const {
    Coverage result;
    std::size_t i = 0;
    std::size_t j = 0;
    while (i < intervals_.size() && j < other.intervals_.size()) {
        const TimeInterval& a = intervals_[i];
        const TimeInterval& b = other.intervals_[j];
        const time::Instant start = a.start < b.start ? b.start : a.start;
        const time::Instant end = a.end < b.end ? a.end : b.end;
        if (start <= end) {
            result.intervals_.push_back({start, end});
        }
        // Advance whichever interval ends first; the other may still overlap the next one.
        if (a.end < b.end) {
            ++i;
        } else {
            ++j;
        }
    }
    return result;
}

Coverage Coverage::union_with(const Coverage& other) const {
    std::vector<TimeInterval> all(intervals_.begin(), intervals_.end());
    all.insert(all.end(), other.intervals_.begin(), other.intervals_.end());
    return of(all);
}

} // namespace usim::ephemeris
