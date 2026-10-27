"""Orientation models from a binary PCK and its frame kernel (P6-05, P6-06; DATA_PACK.md, section ORNT).

A model is the body-fixed frame named in data/catalog/packs.json. With a frame kernel, the kernel defines it
as a TK frame (FRAME_<id>_CLASS = 4) with a constant rotation, given as three angles about coordinate axes,
from a binary PCK frame (FRAME_<id>_CLASS = 2). Without one, the frame is a binary PCK frame that SPICE
defines itself (a built-in frame, for example ITRF93) and there is no offset. The PCK frame's Euler angles
come from the binary PCK's type 2 segments for its class ID, relative to J2000 (the ICRF, as NAIF labels it)
or ECLIPJ2000. For the Moon: MOON_ME_DE440_ME421 relative to MOON_PA_DE440 in moon_de440_250416.tf, with the
angles in moon_pa_de440_200625.bpc. For the Earth: ITRF93 (class ID 3000) from an Earth binary PCK, whose
segments are relative to ECLIPJ2000.

SPICE's TK convention (Frames Required Reading), measured against CSPICE pxform on 2026-10-22: with the
angles a1, a2, a3 about the axes n1, n2, n3, the matrix [a1]_n1 [a2]_n2 [a3]_n3 takes vectors from the TK
frame to its RELATIVE frame, so the rotation from the PCK frame to the body-fixed frame is its transpose.
read_orientation checks this against pxform for every model it reads.
"""

from __future__ import annotations

import math
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

import numpy
import spiceypy

from . import PipelineError
from .kernels import NAIF_FRAME_ECLIPJ2000, NAIF_FRAME_J2000, KernelReader, kernel_pool, read_pck_segments
from .pack.model import AngleUnit, Frame, PackOrientation, PackOrientationSegment, naif

FRAME_CLASS_PCK = 2
FRAME_CLASS_TK = 4
UNITS = {"RADIANS": AngleUnit.RADIANS, "DEGREES": AngleUnit.DEGREES, "ARCSECONDS": AngleUnit.ARCSECONDS}
BASE_FRAMES = {NAIF_FRAME_J2000: Frame.ICRF, NAIF_FRAME_ECLIPJ2000: Frame.ECLIPJ2000}
RADIANS_PER_UNIT = {AngleUnit.RADIANS: 1.0, AngleUnit.DEGREES: math.pi / 180.0, AngleUnit.ARCSECONDS: math.pi / 648000.0}

# Largest accepted element difference between the offset matrix computed from the kernel's angles and
# CSPICE's pxform: a wrong convention (order, sign or transpose) differs by about the angles themselves
# (4e-4 rad for the Moon), rounding by about 1e-16.
MAX_OFFSET_DIFFERENCE = 1.0e-14


@dataclass(frozen=True)
class OrientationDefinition:
    """An entry of a pack's "orientation" list in data/catalog/packs.json."""

    body: int  # NAIF ID
    frame: str  # SPICE name of the body-fixed frame: a TK frame of the frame kernel, or a built-in PCK frame
    pck_source_id: str
    pck_file: str
    frame_kernel_source_id: str | None = None  # None: the frame is a PCK frame SPICE defines itself
    frame_kernel_file: str | None = None

    @property
    def files(self) -> list[tuple[str, str]]:
        """(source ID, file name) of the binary PCK and, if there is one, the frame kernel."""
        files = [(self.pck_source_id, self.pck_file)]
        if self.frame_kernel_source_id is not None and self.frame_kernel_file is not None:
            files.append((self.frame_kernel_source_id, self.frame_kernel_file))
        return files


def parse_definition(value: object, where: str) -> OrientationDefinition:
    keys = {"body", "frame", "pck"}
    if not isinstance(value, dict) or not keys <= set(value) <= keys | {"frame_kernel"}:
        raise PipelineError(f"{where}: an orientation entry needs exactly {sorted(keys)}, and optionally frame_kernel")
    body, frame = value["body"], value["frame"]
    if not isinstance(body, int) or isinstance(body, bool) or not isinstance(frame, str) or not frame:
        raise PipelineError(f"{where}: body must be a NAIF ID and frame a SPICE frame name")
    refs: list[str | None] = []
    for key in ("pck", "frame_kernel"):
        if key not in value:  # only frame_kernel is optional (checked above)
            refs += [None, None]
            continue
        ref = value[key]
        if not isinstance(ref, dict) or set(ref) != {"source_id", "file"}:
            raise PipelineError(f"{where}: {key} must be {{source_id, file}}")
        refs += [str(ref["source_id"]), str(ref["file"])]
    return OrientationDefinition(body, frame, *refs)  # type: ignore[arg-type]


def _rotation(angle: float, axis: int) -> numpy.ndarray:
    """The frame rotation [angle]_axis (SPICE ROTATE): it takes vectors into a frame rotated by `angle`."""
    c, s = math.cos(angle), math.sin(angle)
    if axis == 1:
        return numpy.array([[1.0, 0.0, 0.0], [0.0, c, s], [0.0, -s, c]])
    if axis == 2:
        return numpy.array([[c, 0.0, -s], [0.0, 1.0, 0.0], [s, 0.0, c]])
    return numpy.array([[c, s, 0.0], [-s, c, 0.0], [0.0, 0.0, 1.0]])


def offset_matrix(angles: tuple[float, float, float], axes: tuple[int, int, int], unit: AngleUnit) -> numpy.ndarray:
    """The rotation from the PCK frame to the body-fixed frame (the module documentation)."""
    r = [a * RADIANS_PER_UNIT[unit] for a in angles]
    to_relative = _rotation(r[0], axes[0]) @ _rotation(r[1], axes[1]) @ _rotation(r[2], axes[2])
    return to_relative.T


class _Variables:
    """Variables of one frame kernel, read from its text and cross-checked against CSPICE's pool."""

    def __init__(self, path: Path) -> None:
        self.reader = KernelReader(path)
        self.file = path.name

    def integer(self, name: str) -> int:
        values = self.reader.numbers(name)
        if values is None or len(values) != 1 or values[0] != int(values[0]):
            raise PipelineError(f"{self.file}: {name} must be one integer")
        return int(values[0])

    def numbers(self, name: str, count: int) -> tuple[float, ...]:
        values = self.reader.numbers(name)
        if values is None or len(values) != count:
            raise PipelineError(f"{self.file}: {name} must have {count} values")
        return values

    def string(self, name: str) -> str:
        tokens = self.reader.kernel.text(name)
        if tokens is None or len(tokens) != 1 or not (tokens[0].startswith("'") and tokens[0].endswith("'")):
            raise PipelineError(f"{self.file}: {name} must be one string")
        value = tokens[0][1:-1].replace("''", "'")
        try:
            cspice = spiceypy.gcpool(name, 0, 1)
        except spiceypy.utils.exceptions.NotFoundError:
            raise PipelineError(f"{self.file}: {name} is defined for the parser only") from None
        if list(cspice) != [value]:
            raise PipelineError(f"{self.file}: {name} reads as {value!r}, CSPICE has {list(cspice)!r}")
        return value


def _covers(segments: list[PackOrientationSegment], span: tuple[float, float]) -> bool:
    """True if the segments' coverage, taken together, contains `span`."""
    reached = span[0]
    for s in sorted(segments, key=lambda s: s.start_s):
        if s.start_s > reached:
            break
        reached = max(reached, s.end_s)
    return reached >= span[1]


@dataclass(frozen=True)
class _Offset:
    """A frame kernel's TK frame: its relative (PCK) frame and the constant offset to it."""

    relative: str
    angles: tuple[float, float, float]
    axes: tuple[int, int, int]
    unit: AngleUnit
    frame_id: int


def _tk_frame(definition: OrientationDefinition, frame_kernel_path: Path, where: str) -> tuple[_Offset, int]:
    """The TK frame `definition.frame` of the frame kernel and the class ID of its relative PCK frame."""
    with kernel_pool(frame_kernel_path):
        kernel = _Variables(frame_kernel_path)
        frame_id = kernel.integer(f"FRAME_{definition.frame}")
        if kernel.string(f"FRAME_{frame_id}_NAME") != definition.frame:
            raise PipelineError(f"{where}: FRAME_{frame_id}_NAME is not {definition.frame}")
        if kernel.integer(f"FRAME_{frame_id}_CLASS") != FRAME_CLASS_TK:
            raise PipelineError(f"{where}: {definition.frame} is not a TK frame (class {FRAME_CLASS_TK})")
        if kernel.integer(f"FRAME_{frame_id}_CENTER") != definition.body:
            raise PipelineError(f"{where}: {definition.frame} is not centered on naif:{definition.body}")
        if kernel.string(f"TKFRAME_{frame_id}_SPEC") != "ANGLES":
            raise PipelineError(f"{where}: {definition.frame} is not given by angles (TKFRAME_{frame_id}_SPEC)")
        relative = kernel.string(f"TKFRAME_{frame_id}_RELATIVE")
        angles = kernel.numbers(f"TKFRAME_{frame_id}_ANGLES", 3)
        axis_values = kernel.numbers(f"TKFRAME_{frame_id}_AXES", 3)
        if any(a not in (1.0, 2.0, 3.0) for a in axis_values):
            raise PipelineError(f"{where}: TKFRAME_{frame_id}_AXES must be 1, 2 or 3")
        axes = tuple(int(a) for a in axis_values)
        unit_name = kernel.string(f"TKFRAME_{frame_id}_UNITS")
        if unit_name not in UNITS:
            raise PipelineError(f"{where}: TKFRAME_{frame_id}_UNITS {unit_name} is not one of {sorted(UNITS)}")
        pck_id = kernel.integer(f"FRAME_{relative}")
        if kernel.integer(f"FRAME_{pck_id}_CLASS") != FRAME_CLASS_PCK:
            raise PipelineError(f"{where}: {relative} is not a binary PCK frame (class {FRAME_CLASS_PCK})")
        if kernel.integer(f"FRAME_{pck_id}_CENTER") != definition.body:
            raise PipelineError(f"{where}: {relative} is not centered on naif:{definition.body}")
        class_id = kernel.integer(f"FRAME_{pck_id}_CLASS_ID")
    offset = _Offset(relative, angles, axes, UNITS[unit_name], frame_id)  # type: ignore[arg-type]
    return offset, class_id


def _builtin_pck_frame(definition: OrientationDefinition, where: str) -> int:
    """The class ID of `definition.frame`, which must be a binary PCK frame SPICE defines without kernels."""
    with kernel_pool():
        frame_id = spiceypy.namfrm(definition.frame)
        if frame_id == 0:
            raise PipelineError(f"{where}: {definition.frame} is not a frame SPICE defines without a frame kernel")
        center, frame_class, class_id = (int(v) for v in spiceypy.frinfo(frame_id))
    if frame_class != FRAME_CLASS_PCK:
        raise PipelineError(f"{where}: {definition.frame} is not a binary PCK frame (class {FRAME_CLASS_PCK})")
    if center != definition.body:
        raise PipelineError(f"{where}: {definition.frame} is not centered on naif:{definition.body}")
    return class_id


def read_orientation(
    definition: OrientationDefinition, pck_path: Path, frame_kernel_path: Path | None, window: tuple[float, float],
    log: Callable[[str], None],
) -> PackOrientation:
    """The orientation model of `definition`, with the binary PCK records overlapping `window` (TDB seconds
    past J2000), which they must cover. `frame_kernel_path` is the definition's frame kernel, None without."""
    where = f"orientation of naif:{definition.body}"
    if (frame_kernel_path is None) != (definition.frame_kernel_file is None):
        raise PipelineError(f"{where}: a frame kernel path is needed exactly when the definition names one")
    # Read the frame chain first; the segments are read before the pool loads the PCK, so that DAF handles
    # are not shared.
    if frame_kernel_path is not None:
        offset, class_id = _tk_frame(definition, frame_kernel_path, where)
        pck_frame = offset.relative
    else:
        offset, class_id = None, _builtin_pck_frame(definition, where)
        pck_frame = definition.frame

    pck_segments = read_pck_segments(pck_path, class_id, window)
    segments = []
    for s in pck_segments:
        if s.base_frame not in BASE_FRAMES:
            raise PipelineError(f"{pck_path.name} segment {s.index}: base frame {s.base_frame} is not J2000 or ECLIPJ2000")
        segments.append(
            PackOrientationSegment(
                start_s=s.start_s, end_s=s.end_s, init_s=s.init_s, interval_s=s.interval_s, records=s.records,
                source_id=definition.pck_source_id, locator=f"{s.file_name} segment {s.index} ({s.name})",
                base_frame=BASE_FRAMES[s.base_frame],
            )
        )  # fmt: skip
    if not segments or not _covers(segments, window):
        raise PipelineError(f"{where}: {pck_path.name} does not cover the pack's span for frame class {class_id}")
    bases = sorted({s.base_frame.name for s in segments})
    summary = f"{len(segments)} segments of {pck_path.name} ({pck_frame}, frame class {class_id}, relative to {', '.join(bases)})"

    if offset is None or frame_kernel_path is None:
        log(f"  {definition.frame}: {summary}, no offset (a binary PCK frame)")
        return PackOrientation(
            body=naif(definition.body), frame_name=definition.frame, pck_frame_name=pck_frame,
            offset_angles=(0.0, 0.0, 0.0), offset_axes=(1, 2, 3), offset_unit=AngleUnit.RADIANS,
            offset_source_id=None, offset_locator="", segments=tuple(segments),
        )  # fmt: skip

    # The convention check: the offset from the kernel's angles must equal CSPICE's rotation between the two
    # frames (constant; evaluated where the PCK has data, which pxform needs to route through J2000).
    matrix = offset_matrix(offset.angles, offset.axes, offset.unit)
    with kernel_pool(frame_kernel_path, pck_path):
        cspice = numpy.array(spiceypy.pxform(pck_frame, definition.frame, segments[0].start_s))
    difference = float(numpy.abs(matrix - cspice).max())
    if difference > MAX_OFFSET_DIFFERENCE:
        raise PipelineError(f"{where}: the offset from the frame kernel's angles differs from pxform by {difference}")
    log(f"  {definition.frame}: {summary}, offset from {frame_kernel_path.name} (pxform agrees within {difference:.1e})")
    variables = ", ".join(f"TKFRAME_{offset.frame_id}_{k}" for k in ("RELATIVE", "ANGLES", "AXES", "UNITS"))
    return PackOrientation(
        body=naif(definition.body), frame_name=definition.frame, pck_frame_name=pck_frame,
        offset_angles=offset.angles, offset_axes=offset.axes, offset_unit=offset.unit,
        offset_source_id=definition.frame_kernel_source_id, offset_locator=f"{frame_kernel_path.name} {variables}",
        segments=tuple(segments),
    )  # fmt: skip
