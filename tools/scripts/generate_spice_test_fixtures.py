"""Generate core/tests/fixtures/spice_reference.hpp, the CSPICE oracle for the Phase 1 unit tests.

The output holds two kinds of values, both produced by CSPICE (through SpiceyPy) from the
leap seconds kernel naif0012.tls:

- Time scale data read from the kernel pool: the leap second table (DELTET/DELTA_AT) and the
  TDB - TT model parameters (DELTET/K, DELTET/EB, DELTET/M). The core receives these as data;
  until the data pack exists (Phase 2), the unit tests use this generated copy.
- Reference results: UTC <-> TDB (str2et, et2utc), TDB <-> TT <-> TAI (unitim), calendar
  conversions in the Gregorian, Julian and mixed calendars (str2et with TIMDEF CALENDAR), the
  J2000 -> ECLIPJ2000 rotation (pxform), quaternion conventions (axisar, m2q) and Chebyshev
  evaluation (chbint).

Pinned environment (the script refuses to run with other versions, so the output is reproducible):
    Python 3.12, spiceypy==8.2.0 (CSPICE N0067), numpy==2.5.3

Usage, from the repository root:
    python3 -m venv /tmp/usim-fixtures-venv
    /tmp/usim-fixtures-venv/bin/pip install spiceypy==8.2.0 numpy==2.5.3
    curl -o data/raw/naif/naif0012.tls https://naif.jpl.nasa.gov/pub/naif/generic_kernels/lsk/naif0012.tls
    /tmp/usim-fixtures-venv/bin/python -I tools/scripts/generate_spice_test_fixtures.py \
        --lsk data/raw/naif/naif0012.tls --out core/tests/fixtures/spice_reference.hpp

The Phase 2 pipeline `reference` command (P2-10) will take over this job.
"""

from __future__ import annotations

import argparse
import datetime
import hashlib
import math
import sys
from pathlib import Path

import numpy
import spiceypy as sp

EXPECTED_SPICEYPY = "8.2.0"
EXPECTED_NUMPY = "2.5.3"
EXPECTED_TOOLKIT = "CSPICE_N0067"

LSK_URL = "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/lsk/naif0012.tls"
LSK_SHA256 = "678e32bdb5a744117a467cd9601cd6b373f0e9bc9bbde1371d5eee39600a039b"
LSK_SIZE = 5257

MONTHS = ("JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC")

# UTC epochs around leap seconds, the J2000 epoch and the far future (no leap seconds after the table).
# Fractional seconds are binary fractions (.5, .25, .8125, ...). CSPICE's UTC string parser rounds decimal
# fractions such as .816 or .999 with errors of 0.4 to 1.7 ns (measured 2026-10-20: for whole-second UTC
# epochs str2et agrees with TT + K sin(E(TT)) to 1e-16 s, for "...55.816" it is off by 3.6e-10 s), which
# would hide the comparison the tests make.
UTC_CASES = (
    "1972-01-01T00:00:00",
    "1972-06-30T23:59:59.5",
    "1972-06-30T23:59:60",
    "1972-06-30T23:59:60.75",
    "1972-07-01T00:00:00",
    "1999-12-31T23:59:59.875",
    "2000-01-01T11:58:55.8125",
    "2005-12-31T23:59:60.25",
    "2016-12-31T23:59:59",
    "2016-12-31T23:59:60.5",
    "2017-01-01T00:00:00",
    "2026-10-20T08:15:30.125",
    "2100-03-01T00:00:00",
    "2200-01-01T00:00:00",
)

# TDB seconds past J2000 for TDB <-> TT <-> TAI. The span of de440s.bsp is about +-4.7e9 s.
TDB_CASES = (0.0, 1.0e8, -1.0e8, 536500869.1839298, -3.0e9, 3.0e9, -4.7e9, 4.7e9)

# Calendar dates in astronomical year numbering (year 0 is 1 B.C.). Each is tried in every calendar;
# combinations CSPICE rejects (for example 1900-02-29 in the Gregorian calendar) are recorded as invalid.
CALENDAR_DATES = (
    (2000, 1, 1),
    (1582, 10, 15),
    (1582, 10, 4),
    (1582, 10, 14),
    (-4712, 1, 1),
    (-4713, 12, 31),
    (0, 2, 29),
    (0, 3, 1),
    (1, 1, 1),
    (-1000, 2, 29),
    (-1000, 7, 12),
    (-123, 12, 31),
    (-122, 1, 1),
    (333, 1, 27),
    (837, 4, 10),
    (1600, 2, 29),
    (1700, 2, 29),
    (1900, 2, 29),
    (1957, 10, 4),
    (1987, 1, 27),
    (1988, 6, 19),
    (2100, 2, 28),
    (2400, 2, 29),
    (9999, 12, 31),
)
CALENDARS = (("GREGORIAN", "gregorian"), ("JULIAN", "julian"), ("MIXED", "mixed"))

# Axis-angle rotations for the quaternion convention check (axis need not be unit length).
AXIS_ANGLE_CASES = (((1.0, 2.0, 3.0), 0.7), ((0.0, 0.0, 1.0), math.pi / 2), ((-2.0, 0.5, 1.0), 3.0))

# Chebyshev evaluation: coefficients 1/(k+1) and (-1)^k / (k+1)^2, interval midpoint and radius.
CHEBYSHEV_DEGREE = 12
CHEBYSHEV_INTERVAL = (10.0, 2.0)
CHEBYSHEV_X = (8.0, 8.5, 9.7, 10.0, 11.3, 12.0)


def check_environment() -> None:
    found = (sp.__version__, numpy.__version__, sp.tkvrsn("TOOLKIT"))
    expected = (EXPECTED_SPICEYPY, EXPECTED_NUMPY, EXPECTED_TOOLKIT)
    if found != expected:
        sys.exit(f"error: expected spiceypy/numpy/toolkit {expected}, found {found}")


def check_lsk(path: Path) -> str:
    data = path.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if len(data) != LSK_SIZE or digest != LSK_SHA256:
        sys.exit(f"error: {path} does not match the pinned naif0012.tls (size {len(data)}, sha256 {digest})")
    return digest


def cpp_double(value: float) -> str:
    """Shortest round-trip representation, which a C++ compiler parses to the same double."""
    text = repr(float(value))
    if "inf" in text or "nan" in text:
        raise ValueError(f"non-finite value {value}")
    if "." not in text and "e" not in text:
        text += ".0"
    return text


def spice_date(year: int, month: int, day: int) -> str:
    era_year = f"{year} A.D." if year >= 1 else f"{1 - year} B.C."
    return f"{era_year} {MONTHS[month - 1]} {day} 12:00:00 TDB"


def leap_second_rows() -> list[tuple[int, int, int, int]]:
    values = sp.gdpool("DELTET/DELTA_AT", 0, 1000)
    rows = []
    j2000 = datetime.datetime(2000, 1, 1, 12, 0, 0)
    for i in range(0, len(values), 2):
        tai_minus_utc = values[i]
        # The kernel pool stores @-dates as formal calendar seconds past 2000-01-01T12:00:00.
        when = j2000 + datetime.timedelta(seconds=float(values[i + 1]))
        if when.time() != datetime.time(0, 0, 0) or tai_minus_utc != int(tai_minus_utc):
            sys.exit(f"error: unexpected DELTA_AT entry {tai_minus_utc} at {when}")
        rows.append((when.year, when.month, when.day, int(tai_minus_utc)))
    return rows


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--lsk", type=Path, required=True, help="path to naif0012.tls")
    parser.add_argument("--out", type=Path, required=True, help="output header")
    args = parser.parse_args()

    check_environment()
    digest = check_lsk(args.lsk)
    sp.furnsh(str(args.lsk))

    out: list[str] = []
    emit = out.append
    emit("// Generated by tools/scripts/generate_spice_test_fixtures.py. Do not edit by hand.")
    emit("//")
    emit("// Oracle: CSPICE N0067 through SpiceyPy 8.2.0 (numpy 2.5.3).")
    emit(f"// Input: naif0012.tls, {LSK_URL}")
    emit(f"//        {LSK_SIZE} bytes, SHA-256 {digest}")
    emit("//        (NAIF publishes no checksum list for the lsk directory.)")
    emit("#pragma once")
    emit("")
    emit("#include <cstdint>")
    emit("")
    emit("namespace usim::test::spice {")
    emit("")

    # Time scale data from the kernel pool.
    emit("/// One DELTET/DELTA_AT entry: from 00:00:00 UTC on the date, TAI - UTC is tai_minus_utc_s.")
    emit("struct LeapSecondRow {")
    emit("    std::int32_t year;")
    emit("    std::int32_t month;")
    emit("    std::int32_t day;")
    emit("    std::int32_t tai_minus_utc_s;")
    emit("};")
    emit("")
    emit("/// DELTET/DELTA_AT from naif0012.tls.")
    emit("inline constexpr LeapSecondRow k_leap_seconds[] = {")
    for y, m, d, n in leap_second_rows():
        emit(f"    {{{y}, {m}, {d}, {n}}},")
    emit("};")
    emit("")
    delta_t_a = sp.gdpool("DELTET/DELTA_T_A", 0, 1)[0]
    k = sp.gdpool("DELTET/K", 0, 1)[0]
    eb = sp.gdpool("DELTET/EB", 0, 1)[0]
    m0, m1 = sp.gdpool("DELTET/M", 0, 2)
    emit("/// DELTET/DELTA_T_A (TT - TAI, seconds).")
    emit(f"inline constexpr double k_delta_t_a_s = {cpp_double(delta_t_a)};")
    emit("/// DELTET/K (amplitude of TDB - TT, seconds).")
    emit(f"inline constexpr double k_tdb_k_s = {cpp_double(k)};")
    emit("/// DELTET/EB (eccentricity of the Earth-Moon barycenter orbit).")
    emit(f"inline constexpr double k_tdb_eb = {cpp_double(eb)};")
    emit("/// DELTET/M (mean anomaly at J2000 in radians, and its rate in radians per second).")
    emit(f"inline constexpr double k_tdb_m0_rad = {cpp_double(m0)};")
    emit(f"inline constexpr double k_tdb_m1_rad_s = {cpp_double(m1)};")
    emit("")

    # UTC -> ET (str2et) and back (et2utc, ISOC with 6 decimals).
    sp.timdef("SET", "CALENDAR", 10, "GREGORIAN")
    emit("/// A UTC epoch (ISO 8601, no suffix), its TDB seconds past J2000 from str2et, and et2utc of that value")
    emit("/// in ISOC format with 6 decimals.")
    emit("struct UtcCase {")
    emit("    const char* utc;")
    emit("    double tdb_s;")
    emit("    const char* utc_isoc6;")
    emit("};")
    emit("")
    emit("inline constexpr UtcCase k_utc_cases[] = {")
    for utc in UTC_CASES:
        et = sp.str2et(utc)
        emit(f'    {{"{utc}", {cpp_double(et)}, "{sp.et2utc(et, "ISOC", 6)}"}},')
    emit("};")
    emit("")

    # TDB -> TT and TAI (unitim), and TT -> TDB.
    emit("/// unitim results for a TDB epoch: TT (TDT) and TAI seconds past J2000, and TDB from TT.")
    emit("struct TdbCase {")
    emit("    double tdb_s;")
    emit("    double tt_s;")
    emit("    double tai_s;")
    emit("    double tdb_from_tt_s;")
    emit("};")
    emit("")
    emit("inline constexpr TdbCase k_tdb_cases[] = {")
    for et in TDB_CASES:
        tt = sp.unitim(et, "TDB", "TDT")
        tai = sp.unitim(et, "TDB", "TAI")
        back = sp.unitim(tt, "TDT", "TDB")
        emit(f"    {{{cpp_double(et)}, {cpp_double(tt)}, {cpp_double(tai)}, {cpp_double(back)}}},")
    emit("};")
    emit("")

    # Calendar dates -> Julian day number, per calendar.
    emit("/// A calendar date at 12:00 TDB converted by str2et with TIMDEF CALENDAR set to `calendar`")
    emit("/// (0 Gregorian, 1 Julian, 2 mixed). `valid` is false when CSPICE rejects the date.")
    emit("struct CalendarCase {")
    emit("    std::int32_t calendar;")
    emit("    std::int32_t year;")
    emit("    std::int32_t month;")
    emit("    std::int32_t day;")
    emit("    bool valid;")
    emit("    std::int64_t julian_day_number;")
    emit("};")
    emit("")
    emit("inline constexpr CalendarCase k_calendar_cases[] = {")
    for index, (spice_name, _) in enumerate(CALENDARS):
        sp.timdef("SET", "CALENDAR", 10, spice_name)
        for year, month, day in CALENDAR_DATES:
            try:
                jd = sp.str2et(spice_date(year, month, day)) / 86400.0 + 2451545.0
                if jd != math.floor(jd):
                    sys.exit(f"error: non-integral JD {jd} for {year}-{month}-{day} at noon")
                valid, jdn = "true", int(jd)
            except sp.utils.exceptions.SpiceyError:
                valid, jdn = "false", 0
            emit(f"    {{{index}, {year}, {month}, {day}, {valid}, {jdn}}},")
    sp.timdef("SET", "CALENDAR", 10, "GREGORIAN")
    emit("};")
    emit("")

    # Frames.
    m = sp.pxform("J2000", "ECLIPJ2000", 0.0)
    emit("/// pxform(\"J2000\", \"ECLIPJ2000\", 0): rotates ICRF (J2000) vectors into ECLIPJ2000. Row-major.")
    emit("inline constexpr double k_icrf_to_eclipj2000[3][3] = {")
    for row in m:
        emit("    {" + ", ".join(cpp_double(v) for v in row) + "},")
    emit("};")
    emit("")

    # Quaternions.
    emit("/// axisar(axis, angle) and m2q of that matrix (scalar first: w, x, y, z).")
    emit("struct AxisAngleCase {")
    emit("    double axis[3];")
    emit("    double angle_rad;")
    emit("    double matrix[3][3];")
    emit("    double quat_wxyz[4];")
    emit("};")
    emit("")
    emit("inline constexpr AxisAngleCase k_axis_angle_cases[] = {")
    for axis, angle in AXIS_ANGLE_CASES:
        r = sp.axisar(list(axis), angle)
        q = sp.m2q(r)
        rows = ", ".join("{" + ", ".join(cpp_double(v) for v in row) + "}" for row in r)
        emit(
            "    {{" + ", ".join(cpp_double(v) for v in axis) + "}, " + cpp_double(angle) + ", {" + rows + "}, {"
            + ", ".join(cpp_double(v) for v in q) + "}},"
        )
    emit("};")
    emit("")

    # Chebyshev.
    coeffs = [((-1.0) ** (i % 2)) / ((i + 1) ** 2) if i % 3 == 2 else 1.0 / (i + 1) for i in range(CHEBYSHEV_DEGREE + 1)]
    emit(f"/// Chebyshev coefficients (degree {CHEBYSHEV_DEGREE}) on the interval midpoint {CHEBYSHEV_INTERVAL[0]},")
    emit(f"/// radius {CHEBYSHEV_INTERVAL[1]}, and chbint results (value, derivative with respect to x).")
    emit("inline constexpr double k_chebyshev_coefficients[] = {")
    emit("    " + ", ".join(cpp_double(c) for c in coeffs) + ",")
    emit("};")
    emit(f"inline constexpr double k_chebyshev_midpoint = {cpp_double(CHEBYSHEV_INTERVAL[0])};")
    emit(f"inline constexpr double k_chebyshev_radius = {cpp_double(CHEBYSHEV_INTERVAL[1])};")
    emit("")
    emit("struct ChebyshevCase {")
    emit("    double x;")
    emit("    double value;")
    emit("    double derivative;")
    emit("};")
    emit("")
    emit("inline constexpr ChebyshevCase k_chebyshev_cases[] = {")
    for x in CHEBYSHEV_X:
        value, derivative = sp.chbint(numpy.array(coeffs), CHEBYSHEV_DEGREE, numpy.array(CHEBYSHEV_INTERVAL), x)
        emit(f"    {{{cpp_double(x)}, {cpp_double(value)}, {cpp_double(derivative)}}},")
    emit("};")
    emit("")
    emit("} // namespace usim::test::spice")

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"wrote {args.out}")


if __name__ == "__main__":
    main()
