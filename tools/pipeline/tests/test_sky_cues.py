"""Sky cues (A-07, B-06, ADR-0018): validation of the catalog and the manifest the Unity host reads."""

from __future__ import annotations

import copy
import json
from pathlib import PurePosixPath

import pytest

from usim_pipeline import PipelineError
from usim_pipeline.jsonio import read_json
from usim_pipeline.sky_cues import CUES, HUES, load_sky_cues, parse_sky_cues, unity_manifest
from usim_pipeline.sources import Source

from .conftest import REAL_REPO

# Synthetic sources and cues: the structure is tested, not facts.
SOURCES = (
    Source("S-TEST-A", "t", "p", 1, "https://example.invalid/", "c", "l", ()),
    Source("S-TEST-B", "t", "p", 2, "https://example.invalid/", "c", "l", ()),
)
CUE = {
    "naif_id": 1001,
    "body": "Test Planet",
    "cue": "twilight",
    "hue": "blue",
    "source_id": "S-TEST-A",
    "url": "https://example.invalid/page",
    "accessed": "2026-11-02",
    "reference": "A test page",
    "quote": "A synthetic sentence.",
}
DOCUMENT = {
    "schema_version": 1,
    "description": "test",
    "output": "UniverseSim/Assets/UniverseSim/Data/Test.json",
    "cues": [CUE],
}


def with_cue(**changes: object) -> dict:
    document = copy.deepcopy(DOCUMENT)
    document["cues"][0].update(changes)
    return document


def test_parses_a_cue_and_writes_its_tier_into_the_manifest():
    second = dict(CUE, naif_id=1000, cue="daytime_sky", hue="orange", source_id="S-TEST-B", note="n")
    document = copy.deepcopy(DOCUMENT)
    document["cues"].append(second)
    catalog = parse_sky_cues(document, SOURCES)
    # Sorted by body and cue, so the manifest does not depend on the catalog's order.
    assert [(c.naif_id, c.cue) for c in catalog.cues] == [(1000, "daytime_sky"), (1001, "twilight")]
    manifest = unity_manifest(catalog, SOURCES)
    assert [c["tier"] for c in manifest["cues"]] == [2, 1]
    assert manifest["cues"][1] == {
        "naifId": 1001,
        "body": "Test Planet",
        "cue": "twilight",
        "hue": "blue",
        "sourceId": "S-TEST-A",
        "tier": 1,
        "reference": "A test page",
        "url": "https://example.invalid/page",
        "accessed": "2026-11-02",
        "quote": "A synthetic sentence.",
    }


@pytest.mark.parametrize(
    "changes, message",
    [
        ({"cue": "aurora"}, "unknown cue"),
        ({"hue": "green"}, "unknown hue"),
        ({"source_id": "S-TEST-NONE"}, "not declared"),
        ({"url": "http://example.invalid/"}, "https"),
        ({"accessed": "yesterday"}, "date"),
        ({"quote": " "}, "'quote' is required"),
        ({"naif_id": "1001"}, "integer"),
        ({"naif_id": True}, "integer"),
        ({"color": "blue"}, "unknown keys"),
        ({"note": 3}, "'note' must be text"),
    ],
)
def test_rejects_bad_cues(changes, message):
    with pytest.raises(PipelineError, match=message):
        parse_sky_cues(with_cue(**changes), SOURCES)


def test_rejects_two_cues_of_one_kind_for_a_body():
    document = copy.deepcopy(DOCUMENT)
    document["cues"].append(dict(CUE, hue="orange"))
    with pytest.raises(PipelineError, match="two 'twilight' cues"):
        parse_sky_cues(document, SOURCES)


@pytest.mark.parametrize(
    "output", ["Assets/Data/Test.json", "UniverseSim/Assets/UniverseSim/../x.json", "UniverseSim/Assets/UniverseSim/x.txt"]
)
def test_rejects_an_output_outside_the_unity_project(output):
    document = copy.deepcopy(DOCUMENT)
    document["output"] = output
    with pytest.raises(PipelineError, match="output"):
        parse_sky_cues(document, SOURCES)


def test_the_vocabulary_is_fixed():
    assert set(CUES) == {"daytime_sky", "twilight"}
    assert HUES == ("blue", "red_and_yellow", "brownish_gray", "orange")


def test_the_committed_manifest_matches_the_catalog():
    # The repository's catalog is valid, every cue's source is registered with its tier, and the manifest in the
    # Unity project is what `usim-pipeline sky-cues` writes from it (regenerate it after editing the catalog).
    catalog, sources = load_sky_cues(REAL_REPO)
    assert catalog.cues, "the catalog lists cues"
    path = REAL_REPO.root.joinpath(*PurePosixPath(catalog.output).parts)
    assert read_json(path) == unity_manifest(catalog, sources)
    text = path.read_text(encoding="utf-8")
    assert text == json.dumps(unity_manifest(catalog, sources), indent=2, ensure_ascii=False) + "\n"
