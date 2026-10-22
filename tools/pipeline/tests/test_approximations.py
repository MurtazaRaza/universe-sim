"""Bodies placed at their primary (ADR-0008): the selection's `position` key and the builder's checks."""

from __future__ import annotations

import json

import numpy
import pytest

from usim_pipeline import PipelineError
from usim_pipeline.build import approximate_positions
from usim_pipeline.catalog import Selection, load_selection
from usim_pipeline.pack.model import BodyKind, Frame, PackBody, PackSegment, naif


def _write(tmp_path, bodies):
    path = tmp_path / "bodies.json"
    path.write_text(json.dumps({"schema_version": 1, "bodies": bodies}), encoding="utf-8")
    return path


BASE = [
    {"naif_id": 0, "kind": "barycenter", "primary": None},
    {"naif_id": 4, "kind": "barycenter", "primary": 0},
]


def test_selection_reads_the_position(tmp_path):
    selection = load_selection(_write(tmp_path, [*BASE, {"naif_id": 499, "kind": "planet", "primary": 4, "position": "primary"}]))
    assert [s.position for s in selection] == ["ephemeris", "ephemeris", "primary"]


@pytest.mark.parametrize(
    "entry, message",
    [
        ({"naif_id": 499, "kind": "planet", "primary": 4, "position": "elsewhere"}, "position must be"),
        ({"naif_id": 499, "kind": "planet", "primary": None, "position": "primary"}, "needs a primary"),
        ({"naif_id": 499, "kind": "planet", "primary": 4, "colour": "red"}, "optionally position"),
    ],
)
def test_selection_rejects_bad_positions(tmp_path, entry, message):
    with pytest.raises(PipelineError, match=message):
        load_selection(_write(tmp_path, [*BASE, entry]))


def _segment(target: int, center: int) -> PackSegment:
    records = numpy.array([[0.0, 1.0, 0.0, 0.0, 0.0]])
    return PackSegment(naif(target), naif(center), Frame.ICRF, 2, -1.0, 1.0, -1.0, 2.0, records, "S-TEST", "x")


BODIES = [
    PackBody(naif(0), BodyKind.BARYCENTER, "Root", None),
    PackBody(naif(4), BodyKind.BARYCENTER, "Test Barycenter", naif(0)),
    PackBody(naif(499), BodyKind.PLANET, "Test Planet", naif(4)),
]


def test_approximations_name_the_file_and_the_primary():
    selection = [Selection(0, BodyKind.BARYCENTER, None), Selection(4, BodyKind.BARYCENTER, 0),
                 Selection(499, BodyKind.PLANET, 4, "primary")]  # fmt: skip
    result = approximate_positions(selection, BODIES, [_segment(4, 0)], ["test.bsp"])
    assert len(result) == 1
    assert (result[0].body, result[0].proxy) == (naif(499), naif(4))
    assert result[0].reason == "test.bsp has no segment for Test Planet; it is placed at its primary, Test Barycenter (ADR-0008)"


def test_a_body_with_its_own_segments_cannot_be_approximated():
    selection = [Selection(499, BodyKind.PLANET, 4, "primary")]
    with pytest.raises(PipelineError, match="has ephemeris segments"):
        approximate_positions(selection, BODIES, [_segment(4, 0), _segment(499, 4)], ["test.bsp"])


def test_the_primary_needs_ephemeris():
    selection = [Selection(499, BodyKind.PLANET, 4, "primary")]
    with pytest.raises(PipelineError, match="has no ephemeris"):
        approximate_positions(selection, BODIES, [], ["test.bsp"])
