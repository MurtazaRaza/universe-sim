"""Binary PCK orientation models (P6-05, P6-06): packs.json entries, reading a frame kernel's TK frame and a
binary PCK's type 2 segments, a built-in PCK frame without a frame kernel, the offset and ECLIPJ2000 base frame
conventions against CSPICE, and the coverage check. The kernels here are synthetic (written with CSPICE), not
physical data; the real lunar and Earth kernels are checked in test_real_data.py.
"""

from __future__ import annotations

from pathlib import Path

import numpy
import pytest
import spiceypy

from usim_pipeline import PipelineError
from usim_pipeline.build import load_pack_definitions
from usim_pipeline.kernels import read_pck_segments
from usim_pipeline.orientation import (
    OrientationDefinition,
    _rotation,
    offset_matrix,
    parse_definition,
    read_orientation,
)
from usim_pipeline.pack.model import AngleUnit, Frame, naif

from .conftest import REAL_REPO

CLASS_ID = 1400098
INTERVAL_S = 1000.0
RECORDS = 10
DEGREE = 2


def quiet(_: str) -> None:
    pass


def write_pck(path: Path, first_s: float = 0.0, class_id: int = CLASS_ID, base: str = "J2000") -> numpy.ndarray:
    """A binary PCK with one type 2 segment of synthetic Euler angles; returns the coefficients written."""
    coefficients = numpy.array(
        [[0.1 * (i + 1) + 0.01 * k for k in range(3 * (DEGREE + 1))] for i in range(RECORDS)], dtype=numpy.float64
    )
    handle = spiceypy.pckopn(str(path), "synthetic", 0)
    try:
        spiceypy.pckw02(
            handle, class_id, base, first_s, first_s + RECORDS * INTERVAL_S, "synthetic segment", INTERVAL_S,
            RECORDS, DEGREE, coefficients.reshape(-1), first_s,
        )  # fmt: skip
    finally:
        spiceypy.pckcls(handle)
    return coefficients


def write_frame_kernel(path: Path, **overrides: str) -> None:
    values = {
        "class": "4",
        "spec": "'ANGLES'",
        "units": "'ARCSECONDS'",
        "axes": "( 3, 2, 1 )",
        "relative": "'TEST_PCK'",
        "pck_class": "2",
        "center": "1001",
    }
    values.update(overrides)
    path.write_text(
        "KPL/FK\n\n   Synthetic frame kernel for the pipeline tests.\n\n\\begindata\n\n"
        "   FRAME_TEST_FIXED = 1400099\n"
        "   FRAME_1400099_NAME = 'TEST_FIXED'\n"
        f"   FRAME_1400099_CLASS = {values['class']}\n"
        "   FRAME_1400099_CLASS_ID = 1400099\n"
        f"   FRAME_1400099_CENTER = {values['center']}\n"
        f"   TKFRAME_1400099_SPEC = {values['spec']}\n"
        f"   TKFRAME_1400099_RELATIVE = {values['relative']}\n"
        "   TKFRAME_1400099_ANGLES = ( 100.5 -200.25 50.0 )\n"
        f"   TKFRAME_1400099_AXES = {values['axes']}\n"
        f"   TKFRAME_1400099_UNITS = {values['units']}\n\n"
        "   FRAME_TEST_PCK = 1400098\n"
        "   FRAME_1400098_NAME = 'TEST_PCK'\n"
        f"   FRAME_1400098_CLASS = {values['pck_class']}\n"
        f"   FRAME_1400098_CLASS_ID = {CLASS_ID}\n"
        "   FRAME_1400098_CENTER = 1001\n\n\\begintext\n",
        encoding="utf-8",
    )


DEFINITION = OrientationDefinition(1001, "TEST_FIXED", "S-TEST-A", "test.bpc", "S-TEST-A", "test.tf")

# ITRF93 is a binary PCK frame built into SPICE (Frames Required Reading): class ID 3000, centered on the Earth.
ITRF93_CLASS_ID = 3000
EARTH_DEFINITION = OrientationDefinition(399, "ITRF93", "S-TEST-A", "earth.bpc")


@pytest.fixture
def kernels(tmp_path: Path) -> tuple[Path, Path, numpy.ndarray]:
    pck, fk = tmp_path / "test.bpc", tmp_path / "test.tf"
    coefficients = write_pck(pck)
    write_frame_kernel(fk)
    return pck, fk, coefficients


def test_definitions_are_validated():
    good = {"body": 301, "frame": "F", "pck": {"source_id": "S", "file": "a"}, "frame_kernel": {"source_id": "S", "file": "b"}}
    assert parse_definition(good, "x") == OrientationDefinition(301, "F", "S", "a", "S", "b")
    without = {k: v for k, v in good.items() if k != "frame_kernel"}
    assert parse_definition(without, "x") == OrientationDefinition(301, "F", "S", "a")
    assert parse_definition(without, "x").files == [("S", "a")]
    bad_entries = (
        {**good, "extra": 1}, {**good, "body": "301"}, {**good, "frame": ""}, {**good, "pck": {"file": "a"}},
        {**good, "frame_kernel": None}, {k: v for k, v in good.items() if k != "pck"},
    )  # fmt: skip
    for bad in bad_entries:
        with pytest.raises(PipelineError):
            parse_definition(bad, "x")


def test_the_packs_give_the_moon_and_in_the_moons_pack_the_earth_their_binary_pck_models():
    expected = {
        "solar-system": [(301, "S-NAIF-MOONPA", "S-NAIF-MOONPA")],
        "solar-system-moons": [(301, "S-NAIF-MOONPA", "S-NAIF-MOONPA"), (399, "S-NAIF-EARTHPCK", None)],
    }
    definitions = load_pack_definitions(REAL_REPO.catalog / "packs.json")
    assert {d.name for d in definitions} == set(expected)
    for definition in definitions:
        assert [(o.body, o.pck_source_id, o.frame_kernel_source_id) for o in definition.orientation] == expected[definition.name]


def test_reads_the_frame_chain_and_the_records(kernels):
    pck, fk, coefficients = kernels
    model = read_orientation(DEFINITION, pck, fk, (0.0, RECORDS * INTERVAL_S), quiet)
    assert model.body == naif(1001)
    assert (model.frame_name, model.pck_frame_name) == ("TEST_FIXED", "TEST_PCK")
    assert model.offset_angles == (100.5, -200.25, 50.0)
    assert model.offset_axes == (3, 2, 1) and model.offset_unit == AngleUnit.ARCSECONDS
    assert model.offset_source_id == "S-TEST-A"
    assert model.offset_locator == (
        "test.tf TKFRAME_1400099_RELATIVE, TKFRAME_1400099_ANGLES, TKFRAME_1400099_AXES, TKFRAME_1400099_UNITS"
    )
    assert len(model.segments) == 1
    segment = model.segments[0]
    assert segment.locator == "test.bpc segment 0 (synthetic segment)"
    assert (segment.start_s, segment.end_s, segment.interval_s, segment.record_count) == (0.0, 1.0e4, 1000.0, 10)
    # Each record: MID and RADIUS, then the coefficients exactly as written.
    assert numpy.array_equal(segment.records[:, 2:], coefficients)
    assert numpy.array_equal(segment.records[:, 0], numpy.arange(RECORDS) * INTERVAL_S + INTERVAL_S / 2)


def test_the_offset_matches_cspice(kernels):
    pck, fk, _ = kernels
    model = read_orientation(DEFINITION, pck, fk, (0.0, 1000.0), quiet)
    spiceypy.kclear()
    spiceypy.furnsh(str(fk))
    spiceypy.furnsh(str(pck))
    try:
        cspice = numpy.array(spiceypy.pxform("TEST_PCK", "TEST_FIXED", 500.0))
    finally:
        spiceypy.kclear()
    offset = offset_matrix(model.offset_angles, model.offset_axes, model.offset_unit)
    assert numpy.abs(offset - cspice).max() < 1e-15
    # A different axis order would not match: the check in read_orientation has something to catch.
    assert numpy.abs(offset_matrix(model.offset_angles, (1, 2, 3), model.offset_unit) - cspice).max() > 1e-6


def test_only_the_records_in_the_window_are_kept(kernels):
    pck, fk, coefficients = kernels
    model = read_orientation(DEFINITION, pck, fk, (2500.0, 4200.0), quiet)
    segment = model.segments[0]
    assert (segment.init_s, segment.start_s, segment.end_s, segment.record_count) == (2000.0, 2000.0, 5000.0, 3)
    assert numpy.array_equal(segment.records[:, 2:], coefficients[2:5])


@pytest.mark.parametrize(
    "overrides, message",
    [
        ({"class": "2"}, "not a TK frame"),
        ({"center": "1002"}, "not centered"),
        ({"spec": "'MATRIX'"}, "not given by angles"),
        ({"units": "'HOURANGLE'"}, "UNITS"),
        ({"axes": "( 3, 4, 1 )"}, "AXES"),
        ({"pck_class": "3"}, "not a binary PCK frame"),
    ],
)
def test_rejects_unsupported_frame_kernels(tmp_path, overrides, message):
    pck, fk = tmp_path / "test.bpc", tmp_path / "test.tf"
    write_pck(pck)
    write_frame_kernel(fk, **overrides)
    with pytest.raises(PipelineError, match=message):
        read_orientation(DEFINITION, pck, fk, (0.0, 1000.0), quiet)


def test_a_builtin_pck_frame_needs_no_frame_kernel_and_may_be_relative_to_eclipj2000(tmp_path):
    pck = tmp_path / "earth.bpc"
    coefficients = write_pck(pck, class_id=ITRF93_CLASS_ID, base="ECLIPJ2000")
    model = read_orientation(EARTH_DEFINITION, pck, None, (0.0, RECORDS * INTERVAL_S), quiet)
    assert (model.frame_name, model.pck_frame_name) == ("ITRF93", "ITRF93")
    assert model.offset_source_id is None and model.offset_locator == ""
    assert (model.offset_angles, model.offset_axes, model.offset_unit) == ((0.0, 0.0, 0.0), (1, 2, 3), AngleUnit.RADIANS)
    assert [s.base_frame for s in model.segments] == [Frame.ECLIPJ2000]
    assert numpy.array_equal(model.segments[0].records[:, 2:], coefficients)

    # DATA_PACK.md, section ORNT: relative to ECLIPJ2000, the rotation from the ICRF is R E, with R from the
    # angles. At a record's MID the Chebyshev argument is 0, so each angle of degree 2 is c0 - c2.
    record = model.segments[0].records[3]
    n = DEGREE + 1
    phi, delta, w = (record[2 + k * n] - record[2 + k * n + 2] for k in range(3))
    r = _rotation(w, 3) @ _rotation(delta, 1) @ _rotation(phi, 3)
    spiceypy.kclear()
    spiceypy.furnsh(str(pck))
    try:
        e = numpy.array(spiceypy.pxform("J2000", "ECLIPJ2000", record[0]))
        cspice = numpy.array(spiceypy.pxform("J2000", "ITRF93", record[0]))
    finally:
        spiceypy.kclear()
    assert numpy.abs(r @ e - cspice).max() < 1e-15
    assert numpy.abs(r - cspice).max() > 1e-3  # E matters


@pytest.mark.parametrize(
    "definition, message",
    [
        (OrientationDefinition(399, "NO_SUCH_FRAME", "S-TEST-A", "earth.bpc"), "not a frame SPICE defines"),
        (OrientationDefinition(399, "ECLIPJ2000", "S-TEST-A", "earth.bpc"), "not a binary PCK frame"),
        (OrientationDefinition(301, "ITRF93", "S-TEST-A", "earth.bpc"), "not centered"),
    ],
)
def test_a_builtin_frame_must_be_a_binary_pck_frame_of_the_body(tmp_path, definition, message):
    pck = tmp_path / "earth.bpc"
    write_pck(pck, class_id=ITRF93_CLASS_ID, base="ECLIPJ2000")
    with pytest.raises(PipelineError, match=message):
        read_orientation(definition, pck, None, (0.0, 1000.0), quiet)


def test_segments_must_be_relative_to_j2000_or_eclipj2000(tmp_path):
    pck = tmp_path / "earth.bpc"
    write_pck(pck, class_id=ITRF93_CLASS_ID, base="B1950")
    with pytest.raises(PipelineError, match="not J2000 or ECLIPJ2000"):
        read_orientation(EARTH_DEFINITION, pck, None, (0.0, 1000.0), quiet)


def test_a_frame_kernel_is_passed_exactly_when_the_definition_names_one(kernels):
    pck, fk, _ = kernels
    with pytest.raises(PipelineError, match="exactly when"):
        read_orientation(DEFINITION, pck, None, (0.0, 1000.0), quiet)
    with pytest.raises(PipelineError, match="exactly when"):
        read_orientation(EARTH_DEFINITION, pck, fk, (0.0, 1000.0), quiet)


def test_the_records_must_cover_the_span(kernels):
    pck, fk, _ = kernels
    with pytest.raises(PipelineError, match="does not cover"):
        read_orientation(DEFINITION, pck, fk, (5000.0, 20000.0), quiet)


def test_other_frame_classes_are_skipped(kernels):
    pck, _, _ = kernels
    assert read_pck_segments(pck, CLASS_ID + 1) == []
    assert [s.frame_class_id for s in read_pck_segments(pck, CLASS_ID)] == [CLASS_ID]
