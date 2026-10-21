"""Catalog derivations, segment trimming and manual entries (P2-03, P2-05, P2-06). Synthetic data only."""

from __future__ import annotations

import copy
from decimal import Decimal, getcontext
from fractions import Fraction

import numpy
import pytest

from usim_pipeline import PipelineError
from usim_pipeline.catalog import Selection, apply_manual_entries, display_name, system_barycenter, volumetric_mean_radius
from usim_pipeline.ephemeris import to_pack_segment, trim_records
from usim_pipeline.kernels import SpkSegment
from usim_pipeline.manual import parse_manual_file
from usim_pipeline.pack.model import BodyKind, Field, PackQuantity, Unit, naif
from usim_pipeline.sources import Source


def test_volumetric_mean_radius_is_correctly_rounded():
    for a, b, c in ((3.5, 2.5, 1.5), (7.25, 7.25, 6.0), (1.0, 2.0, 4.0)):
        r = volumetric_mean_radius(a, b, c)
        # r is the double nearest the true root: its neighbours' cubes bracket a*b*c from farther away.
        target = Fraction(a) * Fraction(b) * Fraction(c)
        error = abs(Fraction(r) ** 3 - target)
        for neighbour in (numpy.nextafter(r, 0.0), numpy.nextafter(r, 10.0)):
            assert abs(Fraction(float(neighbour)) ** 3 - target) > error
    assert volumetric_mean_radius(1.0, 2.0, 4.0) == 2.0


def test_names_and_barycenters():
    assert display_name("SOLAR SYSTEM BARYCENTER") == "Solar System Barycenter"
    assert display_name("SUN") == "Sun"
    assert [system_barycenter(i) for i in (199, 301, 399, 599, 999, 10, 3, 2000001)] == [1, 3, 3, 5, 9, 10, 3, 2000001]


def segment(count: int = 10, init: float = 0.0, interval: float = 100.0) -> SpkSegment:
    records = numpy.arange(count * 5, dtype=numpy.float64).reshape(count, 5)
    return SpkSegment("synthetic.bsp", 0, "SYN", 1001, 1000, 1, 2, init, init + count * interval, init, interval, 5, records, 1)


@pytest.mark.parametrize(
    "window, expected",
    [
        (None, (0, 9)),
        ((250.0, 420.0), (2, 4)),
        ((200.0, 400.0), (2, 3)),  # boundaries fall exactly between records
        ((-500.0, 50.0), (0, 0)),
        ((950.0, 5000.0), (9, 9)),
        ((1000.0, 2000.0), None),
        ((-200.0, 0.0), None),
    ],
)
def test_trim_records(window, expected):
    assert trim_records(segment(), window) == expected


def test_trimmed_segment_keeps_records_bit_for_bit():
    s = segment()
    trimmed = to_pack_segment(s, "S-TEST-A", (250.0, 420.0))
    assert trimmed is not None
    assert (trimmed.init_s, trimmed.start_s, trimmed.end_s) == (200.0, 200.0, 500.0)
    assert trimmed.records.tobytes() == s.records[2:5].tobytes()
    assert trimmed.locator == "synthetic.bsp segment 0 (SYN)"


def test_unsupported_frames_are_rejected():
    s = segment()
    s = SpkSegment(**{**s.__dict__, "frame": 17})
    with pytest.raises(PipelineError, match="frame code 17"):
        to_pack_segment(s, "S-TEST-A", None)


SOURCES = (Source("S-TEST-A", "t", "p", 1, "https://example.invalid/", "c", "l", ()),)
ENTRY = {
    "naif_id": 1001,
    "field": "gm",
    "value": 1.25,
    "unit": "km3_per_s2",
    "uncertainty": 0.5,
    "source_id": "S-TEST-A",
    "url": "https://example.invalid/table.html",
    "accessed": "2026-10-20",
    "table": "Table 1, row Test Planet",
    "transcriber_note": "Synthetic entry for the test.",
}


def manual(**changes):
    entry = {**copy.deepcopy(ENTRY), **changes}
    for key in [k for k, v in changes.items() if v is ...]:
        del entry[key]
    return {"schema_version": 1, "entries": [entry]}


def test_valid_manual_entry():
    (entry,) = parse_manual_file(manual(), "manual/test.json", SOURCES)
    assert (entry.field, entry.unit, entry.value, entry.uncertainty) == (Field.GM, Unit.KM3_PER_S2, 1.25, 0.5)
    assert "accessed 2026-10-20" in entry.locator and "data/catalog/manual/test.json" in entry.locator


@pytest.mark.parametrize("missing", ["source_id", "url", "accessed", "table", "transcriber_note"])
def test_manual_entry_without_required_provenance_is_rejected(missing):
    with pytest.raises(PipelineError, match=missing):
        parse_manual_file(manual(**{missing: ...}), "manual/test.json", SOURCES)
    with pytest.raises(PipelineError, match=missing):
        parse_manual_file(manual(**{missing: "  "}), "manual/test.json", SOURCES)


@pytest.mark.parametrize(
    "changes, message",
    [
        ({"source_id": "S-UNDECLARED"}, "not declared"),
        ({"unit": "km"}, "needs unit"),
        ({"field": "albedo"}, "unknown field"),
        ({"value": "1.0"}, "finite number"),
        ({"uncertainty": -1.0}, "negative"),
        ({"url": "http://example.invalid/"}, "https"),
        ({"accessed": "20 Oct 2026"}, "date"),
        ({"extra": 1}, "unknown keys"),
    ],
)
def test_invalid_manual_entries(changes, message):
    with pytest.raises(PipelineError, match=message):
        parse_manual_file(manual(**changes), "manual/test.json", SOURCES)


def test_manual_entry_cannot_override_a_kernel_value():
    (entry,) = parse_manual_file(manual(), "manual/test.json", SOURCES)
    selection = [Selection(1001, BodyKind.PLANET, None)]
    from_kernel = [PackQuantity(naif(1001), Field.GM, Unit.KM3_PER_S2, 2.0, None, "S-TEST-A", "kernel")]
    with pytest.raises(PipelineError, match="already provided by a kernel"):
        apply_manual_entries(selection, from_kernel, [entry])
    added = apply_manual_entries(selection, [], [entry])
    assert added[0].value == 1.25 and added[0].source_id == "S-TEST-A"
    assert apply_manual_entries([Selection(5, BodyKind.PLANET, None)], [], [entry]) == []


def test_decimal_context_is_not_affected_globally():
    before = getcontext().prec
    volumetric_mean_radius(3.0, 2.0, 1.0)
    assert getcontext().prec == before and Decimal(1) / Decimal(3) == Decimal(1) / Decimal(3)
