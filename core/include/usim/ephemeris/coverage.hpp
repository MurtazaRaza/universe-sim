#pragma once

#include "usim/time/time_point.hpp"

#include <optional>
#include <span>
#include <vector>

namespace usim::ephemeris {

/// A closed interval of TDB instants, [start, end].
struct TimeInterval {
    time::Instant start; ///< First covered instant (inclusive).
    time::Instant end;   ///< Last covered instant (inclusive).

    /// True if start <= t <= end.
    [[nodiscard]] bool contains(time::Instant t) const noexcept { return start <= t && t <= end; }

    friend bool operator==(const TimeInterval&, const TimeInterval&) noexcept = default;
};

/// The instants at which data exists (SPEC FR-SIM-03): a set of disjoint closed TDB intervals, sorted by
/// time. Intervals that overlap or touch are merged, so a gap means a real absence of data.
class Coverage {
public:
    /// Empty coverage.
    Coverage() = default;

    /// The union of `intervals` (in any order). Intervals with end < start or an invalid instant are
    /// ignored.
    [[nodiscard]] static Coverage of(std::span<const TimeInterval> intervals);

    /// The disjoint intervals, sorted by time.
    [[nodiscard]] std::span<const TimeInterval> intervals() const noexcept { return intervals_; }

    /// True if no instant is covered.
    [[nodiscard]] bool empty() const noexcept { return intervals_.empty(); }

    /// True if `t` lies in one of the intervals. O(log n).
    [[nodiscard]] bool contains(time::Instant t) const noexcept;

    /// The first covered instant to the last, ignoring gaps; nothing if empty.
    [[nodiscard]] std::optional<TimeInterval> span() const noexcept;

    /// The instants covered by both this and `other`.
    [[nodiscard]] Coverage intersection(const Coverage& other) const;

    /// The instants covered by this or `other`.
    [[nodiscard]] Coverage union_with(const Coverage& other) const;

    friend bool operator==(const Coverage&, const Coverage&) noexcept = default;

private:
    std::vector<TimeInterval> intervals_;
};

} // namespace usim::ephemeris
