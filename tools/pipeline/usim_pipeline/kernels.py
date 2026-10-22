"""Reading NAIF kernels through SpiceyPy (CSPICE), plus a small text kernel parser.

Numeric values of text kernels are taken from the decimal text in the file, rounded correctly to double
(Python's float()), and cross-checked against CSPICE's kernel pool: the variable must exist in both with the
same number of values, and each value must agree to within MAX_PARSE_ULPS units in the last place. CSPICE's
own parser is not correctly rounded (measured 2026-10-20 over gm_de440.tpc and pck00011.tpc: 45 of 3123
values differ, by 1 to 5 ulps; the largest is BODY5_GM), so the pack keeps the correctly rounded value of
the text the publisher wrote (ADR-0007).

Text kernels are loaded one at a time (`kernel_pool`), so every value is known to come from that one file.
SPK segments are read with the DAF routines and copied bit for bit.
"""

from __future__ import annotations

import datetime
import math
import re
import struct
from collections.abc import Callable, Iterator
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path

import numpy
import spiceypy
import spiceypy.utils.exceptions

from . import PipelineError

EXPECTED_SPICEYPY = "8.2.0"
EXPECTED_NUMPY = "2.5.3"
EXPECTED_TOOLKIT = "CSPICE_N0067"

# SPK data types the pack supports (Chebyshev position, and position and velocity), and their number of
# Chebyshev coefficient sets per record.
SPK_COMPONENTS = {2: 3, 3: 6}

NAIF_FRAME_J2000 = 1  # NAIF frame code of J2000, which SPICE uses to label the ICRF


def toolkit_versions() -> dict[str, str]:
    """Versions of the libraries the pipeline's results depend on."""
    return {
        "spiceypy": spiceypy.__version__,
        "numpy": numpy.__version__,
        "toolkit": spiceypy.tkvrsn("TOOLKIT"),
    }


def check_environment() -> None:
    """Fails unless the pinned SpiceyPy, numpy and CSPICE versions are installed, so outputs are reproducible."""
    found = toolkit_versions()
    expected = {"spiceypy": EXPECTED_SPICEYPY, "numpy": EXPECTED_NUMPY, "toolkit": EXPECTED_TOOLKIT}
    if found != expected:
        raise PipelineError(f"expected {expected}, found {found}; install tools/pipeline/requirements.txt")


@contextmanager
def kernel_pool(*paths: Path) -> Iterator[None]:
    """Loads exactly the given kernels into an empty kernel pool for the duration of the block."""
    spiceypy.kclear()
    try:
        for path in paths:
            spiceypy.furnsh(str(path))
        yield
    finally:
        spiceypy.kclear()


def pool_doubles(name: str) -> tuple[float, ...] | None:
    """The numeric values of kernel pool variable `name`, or None if it is not defined."""
    try:
        count, kind = spiceypy.dtpool(name)
    except spiceypy.utils.exceptions.NotFoundError:
        return None
    if kind != "N":
        raise PipelineError(f"kernel variable {name} is not numeric")
    return tuple(float(v) for v in spiceypy.gdpool(name, 0, count))


def pool_names(pattern: str) -> tuple[str, ...]:
    """Names of kernel pool variables matching a SPICE wildcard pattern (* and %), sorted."""
    try:
        return tuple(sorted(spiceypy.gnpool(pattern, 0, 10000, 80)))
    except spiceypy.utils.exceptions.NotFoundError:
        return ()


_ASSIGNMENT = re.compile(r"\s*([^\s=+]+)\s*(\+?=)\s*")
_MONTHS = {m: i + 1 for i, m in enumerate(("JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"))}
_AT_DATE = re.compile(r"@(\d{4})-([A-Z]{3})-(\d{1,2})")


def _tokens(text: str, position: int) -> tuple[list[str], int]:
    """Values of one assignment starting at `position`: a single token or a parenthesized list (which may
    span lines). Strings are quoted with ' ('' escapes a quote). Returns the tokens and the end position."""
    i = position
    while i < len(text) and text[i] in " \t":
        i += 1
    if i < len(text) and text[i] != "(":
        match = re.compile(r"'(?:[^']|'')*'|[^\s,]+").match(text, i)
        if not match:
            raise PipelineError("text kernel: assignment without a value")
        return [match.group(0)], match.end()
    end = i + 1
    tokens: list[str] = []
    token = re.compile(r"\s*,?\s*('(?:[^']|'')*'|[^\s,()']+)")
    while True:
        closing = re.compile(r"[\s,]*\)").match(text, end)
        if closing:
            return tokens, closing.end()
        match = token.match(text, end)
        if not match or match.end() == end:
            raise PipelineError("text kernel: unterminated value list")
        tokens.append(match.group(1))
        end = match.end()


class TextKernel:
    """The data of a NAIF text kernel (Kernel Required Reading): assignments inside \\begindata sections,
    with = replacing and += appending. Values are kept as written in the file."""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.variables: dict[str, list[str]] = {}
        text = path.read_text(encoding="utf-8", errors="strict")
        sections = re.split(r"^[ \t]*(\\begindata|\\begintext)[ \t]*$", text, flags=re.M)
        in_data = False
        for part in sections:
            if part in ("\\begindata", "\\begintext"):
                in_data = part == "\\begindata"
                continue
            if not in_data:
                continue
            position = 0
            while True:
                match = _ASSIGNMENT.search(part, position)
                if not match:
                    break
                tokens, position = _tokens(part, match.end())
                name, operator = match.group(1), match.group(2)
                if operator == "=":
                    self.variables[name] = tokens
                else:
                    self.variables.setdefault(name, []).extend(tokens)

    def text(self, name: str) -> list[str] | None:
        """The values of `name` exactly as written, or None if the kernel does not define it."""
        return self.variables.get(name)


def parse_number(token: str) -> float:
    """A NAIF numeric token (Fortran D or E exponent) correctly rounded to double."""
    try:
        return float(token.replace("D", "E").replace("d", "e"))
    except ValueError:
        raise PipelineError(f"text kernel: {token!r} is not a number") from None


def parse_at_date(token: str) -> tuple[int, int, int]:
    """An @-date token of the form @YYYY-MON-DD (as in DELTET/DELTA_AT) as (year, month, day)."""
    match = _AT_DATE.fullmatch(token.upper())
    if not match or match.group(2) not in _MONTHS:
        raise PipelineError(f"text kernel: {token!r} is not an @-date of the form @1972-JAN-1")
    return int(match.group(1)), _MONTHS[match.group(2)], int(match.group(3))


def _formal_seconds(date: tuple[int, int, int]) -> float:
    """Formal calendar seconds past 2000-01-01T12:00:00, which is how the kernel pool stores @-dates."""
    delta = datetime.datetime(*date) - datetime.datetime(2000, 1, 1, 12)
    return float(delta.days * 86400 + delta.seconds)


# Largest accepted distance, in units in the last place, between the correctly rounded value of a kernel's
# text and CSPICE's reading of it. Measured maximum: 5 ulps (gm_de440.tpc BODY5_GM, 2026-10-20). A parsing
# mistake (wrong token, missed exponent) would be off by many orders of magnitude more.
MAX_PARSE_ULPS = 16


@dataclass(frozen=True)
class KernelValue:
    """A numeric value from a text kernel: the correctly rounded value of the text, the text as written,
    and CSPICE's reading of it (equal, or at most MAX_PARSE_ULPS away)."""

    value: float
    text: str
    cspice: float


def _ordered(x: float) -> int:
    bits = struct.unpack("<q", struct.pack("<d", x))[0]
    return bits if bits >= 0 else -(bits & 0x7FFFFFFFFFFFFFFF)


def ulp_distance(a: float, b: float) -> int:
    """Number of representable doubles between a and b (0 if equal; +0.0 and -0.0 are equal)."""
    return abs(_ordered(a) - _ordered(b))


def kernel_values(kernel: TextKernel, name: str) -> tuple[KernelValue, ...] | None:
    """Numeric values of `name` from the kernel text, cross-checked against CSPICE's kernel pool, which
    must hold exactly this one kernel (see `kernel_pool`). None if the variable is not defined.
    @-dates are returned as formal calendar seconds past 2000-01-01T12:00:00, as the kernel pool stores them.
    """
    tokens = kernel.text(name)
    pool = pool_doubles(name)
    if tokens is None or pool is None:
        if tokens is None and pool is None:
            return None
        raise PipelineError(f"{kernel.path.name}: {name} is defined for {'CSPICE' if pool else 'the parser'} only")
    if len(tokens) != len(pool):
        raise PipelineError(f"{kernel.path.name}: {name} has {len(tokens)} values in the text, {len(pool)} in CSPICE")
    values = []
    for token, cspice in zip(tokens, pool):
        value = _formal_seconds(parse_at_date(token)) if token.startswith("@") else parse_number(token)
        if ulp_distance(value, cspice) > MAX_PARSE_ULPS:
            raise PipelineError(f"{kernel.path.name}: {name} value {token} reads as {value!r}, CSPICE has {cspice!r}")
        values.append(KernelValue(value, token, cspice))
    return tuple(values)


class KernelReader:
    """Reads numeric variables of one text kernel through `kernel_values`; use inside `kernel_pool` of
    that kernel. Counts the values CSPICE rounds differently, for the build report."""

    def __init__(self, path: Path) -> None:
        self.kernel = TextKernel(path)
        self.values_read = 0
        self.rounding_differences = 0
        self.largest_difference_ulps = 0

    @property
    def file_name(self) -> str:
        return self.kernel.path.name

    def defines(self, name: str) -> bool:
        return name in self.kernel.variables or bool(pool_names(name))

    def numbers(self, name: str) -> tuple[float, ...] | None:
        values = kernel_values(self.kernel, name)
        if values is None:
            return None
        self.values_read += len(values)
        self.rounding_differences += sum(1 for v in values if v.value != v.cspice)
        self.largest_difference_ulps = max([self.largest_difference_ulps, *(ulp_distance(v.value, v.cspice) for v in values)])
        return tuple(v.value for v in values)

    def report(self) -> str:
        return (
            f"{self.file_name}: {self.values_read} values read from the file text; "
            f"{self.rounding_differences} read differently by CSPICE (up to {self.largest_difference_ulps} ulps; the "
            "pack keeps the correctly rounded value of the text)"
        )


def body_name(naif_id: int) -> str:
    """The NAIF name of a body from the toolkit's built-in table (for example "EARTH BARYCENTER")."""
    try:
        return str(spiceypy.bodc2n(naif_id))
    except spiceypy.utils.exceptions.NotFoundError:
        raise PipelineError(f"NAIF ID {naif_id} has no name in the toolkit's table") from None


@dataclass(frozen=True)
class SpkSegment:
    """One SPK segment of type 2 or 3, as stored in the file.

    Each record holds MID and RADIUS (seconds) followed by the Chebyshev coefficients of X, Y, Z (and for
    type 3 VX, VY, VZ), each `degree + 1` long. Times are TDB seconds past J2000.
    """

    file_name: str
    index: int  # position of the segment in the file (later segments take precedence in SPICE)
    name: str  # segment identifier
    target: int
    center: int
    frame: int  # NAIF frame code
    data_type: int
    start_s: float  # coverage from the segment summary
    end_s: float
    init_s: float  # start of the first record held
    interval_s: float  # length of every record
    record_size: int  # doubles per record
    records: numpy.ndarray  # float64, shape (record_count, record_size): all records, or those in a window
    first_address: int  # DAF address (1-based, in doubles) of the first record held

    @property
    def record_count(self) -> int:
        return int(self.records.shape[0])

    @property
    def degree(self) -> int:
        return (self.record_size - 2) // SPK_COMPONENTS[self.data_type] - 1


def _exact_int(value: float, what: str) -> int:
    if value != int(value):
        raise PipelineError(f"{what} is not an integer: {value}")
    return int(value)


def record_range(
    init_s: float, interval_s: float, count: int, start_s: float, end_s: float, window: tuple[float, float] | None
) -> tuple[int, int] | None:
    """Indices [first, last] of the records of a segment that overlap `window` (TDB seconds past J2000): all
    of them for no window, None if the segment's coverage [start_s, end_s] does not reach into the window."""
    if window is None:
        return (0, count - 1)
    start, end = window
    first = max(0, math.floor((start - init_s) / interval_s))
    last = min(count - 1, math.ceil((end - init_s) / interval_s) - 1)
    if first > last or start >= end_s or end <= start_s:
        return None
    return (first, last)


def read_spk_segments(
    path: Path, keep: Callable[[int, int], bool] | None = None, window: tuple[float, float] | None = None
) -> list[SpkSegment]:
    """The segments of an SPK file, in file order. Fails on data types other than 2 and 3.

    `keep(target, center)` selects segments (all by default). With a `window` (TDB seconds past J2000) only
    the records overlapping it are read: a segment is returned with those records, its coverage clipped to
    them, and a segment that does not reach into the window is left out. This keeps memory use to what the
    pack needs (the satellite kernels are over 1 GB).
    """
    handle = spiceypy.dafopr(str(path))
    segments: list[SpkSegment] = []
    index = -1
    try:
        spiceypy.dafbfs(handle)
        while spiceypy.daffna():
            index += 1
            summary = spiceypy.dafgs(5)  # ND = 2 doubles + NI = 6 integers packed into 3 doubles
            dc, ic = spiceypy.dafus(summary, 2, 6)
            name = spiceypy.dafgn().strip()
            target, center, frame, data_type, begin, end = (int(v) for v in ic)
            if keep is not None and not keep(target, center):
                continue
            where = f"{path.name} segment {index} ({target} wrt {center})"
            if data_type not in SPK_COMPONENTS:
                raise PipelineError(f"{where}: SPK type {data_type} is not supported (only 2 and 3)")
            init, interval, rsize, count = (float(v) for v in spiceypy.dafgda(handle, end - 3, end))
            record_size = _exact_int(rsize, f"{where}: RSIZE")
            record_count = _exact_int(count, f"{where}: N")
            components = SPK_COMPONENTS[data_type]
            if record_size < 2 + components or (record_size - 2) % components != 0:
                raise PipelineError(f"{where}: record size {record_size} does not fit SPK type {data_type}")
            if end - begin + 1 != record_size * record_count + 4:
                raise PipelineError(f"{where}: segment length does not match {record_count} records of {record_size}")
            if not interval > 0:
                raise PipelineError(f"{where}: record interval {interval} is not positive")
            start_s, end_s = float(dc[0]), float(dc[1])
            kept = record_range(init, interval, record_count, start_s, end_s, window)
            if kept is None:
                continue
            first, last = kept
            address = begin + first * record_size
            data = spiceypy.dafgda(handle, address, begin + (last + 1) * record_size - 1)
            records = numpy.array(data, dtype=numpy.float64).reshape(last - first + 1, record_size)
            kept_init = init + first * interval
            segments.append(
                SpkSegment(
                    file_name=path.name,
                    index=index,
                    name=name,
                    target=target,
                    center=center,
                    frame=frame,
                    data_type=data_type,
                    start_s=max(start_s, kept_init),
                    end_s=min(end_s, init + (last + 1) * interval),
                    init_s=kept_init,
                    interval_s=interval,
                    record_size=record_size,
                    records=records,
                    first_address=address,
                )
            )
    finally:
        spiceypy.dafcls(handle)
    return segments
