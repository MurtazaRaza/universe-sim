#pragma once

#include "usim/catalog/body_id.hpp"
#include "usim/catalog/quantity.hpp"
#include "usim/frames/frame_id.hpp"
#include "usim/frames/frame_transform.hpp"
#include "usim/math/mat3.hpp"
#include "usim/result.hpp"
#include "usim/time/time_point.hpp"

#include <array>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace usim::frames {

/// Unit of the offset angles of a PckOrientation (DATA_PACK.md, section ORNT): the frame kernel's
/// TKFRAME_<id>_UNITS. Values are stable and stored in packs.
enum class AngleUnit : std::uint32_t {
    radians = 0,
    degrees = 1,
    arcseconds = 2,
};

/// The Chebyshev records of one binary PCK type 2 segment (PCK Required Reading; DATA_PACK.md, sections ORNT
/// and EPHD), copied bit for bit from the PCK file.
///
/// Record i covers [init_tdb_s + i interval_s, init_tdb_s + (i + 1) interval_s] and holds record_size
/// doubles: MID and RADIUS (TDB seconds; the record's time argument is (t - MID) / RADIUS), then the
/// Chebyshev coefficients of the Euler angles phi, delta and w in radians, each degree() + 1 long. The angles
/// give the rotation from base_frame to the PCK frame, [w]_3 [delta]_1 [phi]_3 (see euler_313_transform), and
/// their rates are the derivatives of the polynomials.
struct PckSegment {
    FrameKind base_frame = FrameKind::icrf; ///< Frame of the angles: the ICRF or ECLIPJ2000 (Earth PCKs).
    double start_tdb_s = 0.0;       ///< Start of coverage, TDB seconds past J2000 (inclusive).
    double end_tdb_s = 0.0;         ///< End of coverage, TDB seconds past J2000 (inclusive).
    double init_tdb_s = 0.0;        ///< Start of the first record, TDB seconds past J2000.
    double interval_s = 0.0;        ///< Length of every record, seconds.
    std::uint32_t record_size = 0;  ///< Doubles per record.
    std::uint32_t record_count = 0; ///< Number of records.
    std::vector<double> records;    ///< record_count * record_size doubles, record after record.
    catalog::SourceRef source;      ///< The PCK file and segment the records were copied from.

    /// Degree of the Chebyshev polynomials.
    [[nodiscard]] std::uint32_t degree() const noexcept { return (record_size - 2) / 3 - 1; }
};

/// A body's orientation from a SPICE binary PCK and, where the body-fixed frame is defined relative to the
/// PCK frame, a frame kernel (P6-05, P6-06; DATA_PACK.md, section ORNT). The Moon: MOON_ME_DE440_ME421 from
/// moon_pa_de440_200625.bpc and moon_de440_250416.tf. The Earth (moons pack): ITRF93 from an Earth binary
/// PCK, without a frame kernel.
///
/// The segments give the PCK frame relative to the ICRF or ECLIPJ2000. The frame kernel defines the
/// body-fixed frame as a constant offset from the PCK frame, three angles about coordinate axes in the SPICE
/// TK convention: the matrix [angle_1]_axis_1 [angle_2]_axis_2 [angle_3]_axis_3 takes vectors from the
/// body-fixed frame to the PCK frame, so the rotation from the PCK frame to the body-fixed frame is its
/// transpose (pck_offset(); checked against CSPICE pxform by the pipeline and by VAL-04). Without an offset
/// (no offset_source), the body-fixed frame is the PCK frame.
struct PckOrientation {
    catalog::BodyId body;                         ///< The body this model orients.
    std::string frame_name;                       ///< SPICE name of the body-fixed frame (for display).
    std::string pck_frame_name;                   ///< SPICE name of the segments' frame (for display).
    std::vector<PckSegment> segments;             ///< In precedence order: later segments win on overlap.
    std::array<double, 3> offset_angles{};        ///< The offset angles, in offset_unit.
    std::array<std::int32_t, 3> offset_axes{1, 2, 3}; ///< The axis of each offset angle: 1, 2 or 3.
    AngleUnit offset_unit = AngleUnit::radians;   ///< Unit of offset_angles.
    /// The frame kernel and the variables of the offset; nothing without an offset.
    std::optional<catalog::SourceRef> offset_source;
};

/// Checks a model: at least one segment; records matching each segment's layout (type 2: record_size =
/// 2 + 3 (degree + 1)); init <= start < end <= init + record_count interval (the last within a
/// microsecond, DATA_PACK.md section ORNT; the last record is used up to the end); every record's RADIUS
/// positive and its interval [MID - RADIUS, MID + RADIUS] containing the instants it is used for (within a
/// microsecond, as for SPK segments); base frames ICRF or ECLIPJ2000; finite offset angles about axes 1 to
/// 3; without an offset, the PCK frame's name, zero angles about the axes 1, 2, 3, in radians. Fails with
/// ErrorCode::data_format.
[[nodiscard]] Result<void> check_pck_orientation(const PckOrientation& model);

/// The rotation from the model's PCK frame to its body-fixed frame (constant; the identity without an
/// offset).
[[nodiscard]] math::Mat3 pck_offset(const PckOrientation& model) noexcept;

/// The transform from the ICRF to the model's PCK frame at `t` (TDB), from the last segment covering `t`
/// (through ECLIPJ2000 for a segment relative to it: R E, with E = icrf_to_eclipj2000()). Nothing outside the
/// segments' coverage or for an invalid instant: the model never extrapolates.
[[nodiscard]] std::optional<FrameTransform> pck_frame_transform(const PckOrientation& model,
                                                               time::Instant t) noexcept;

/// The transform from the ICRF to the model's body-fixed frame at `t` (TDB): pck_frame_transform() followed
/// by pck_offset() if the model has an offset. Equals SPICE sxform("J2000", frame_name, t). Nothing outside
/// the coverage.
[[nodiscard]] std::optional<FrameTransform> pck_transform(const PckOrientation& model,
                                                         time::Instant t) noexcept;

/// True if a segment of the model covers `t`.
[[nodiscard]] bool pck_covers(const PckOrientation& model, time::Instant t) noexcept;

} // namespace usim::frames
