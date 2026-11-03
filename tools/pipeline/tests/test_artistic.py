"""Artistic assets (A-04, ADR-0015 decision 6): the catalog's rules, the check against the Art folder, and the
ATTRIBUTION.md section, rewritten alone and identical to what `textures` writes."""

from __future__ import annotations

import copy

import pytest

from usim_pipeline import PipelineError
from usim_pipeline.artistic import (
    SECTION_HEADING,
    attribution_section,
    check_files,
    load_artistic_assets,
    parse_artistic_assets,
    splice_attribution,
)
from usim_pipeline.repo import Repo
from usim_pipeline.textures import ATTRIBUTION_PATH

from .conftest import REAL_REPO

# Synthetic entries: the structure is tested, not real art.
ICON = {
    "file": "Art/Icons/test-icon.png",
    "kind": "icon",
    "title": "Test icon",
    "author": "A. Tester",
    "origin": "original",
    "license": "CC BY 4.0",
    "license_url": "https://creativecommons.org/licenses/by/4.0/",
}
IMPRESSION = {
    "file": "Art/Impressions/test-body.png",
    "kind": "artist_impression",
    "title": "Test body, banded",
    "author": "A. Painter",
    "origin": "original",
    "license": "CC BY 4.0",
    "body": 1001,
    "body_name": "Test Planet",
    "references": [{"title": "A test image", "url": "https://example.invalid/image", "credit": "Test Mission"}],
}


def document(*assets: dict) -> dict:
    return {"schema_version": 1, "description": "test", "assets": [copy.deepcopy(a) for a in assets]}


def test_parses_assets_sorted_by_file_and_writes_their_section():
    assets = parse_artistic_assets(document(IMPRESSION, ICON))
    assert [a.file for a in assets] == ["Art/Icons/test-icon.png", "Art/Impressions/test-body.png"]
    text = "\n".join(attribution_section(assets))
    assert text.startswith(SECTION_HEADING)
    assert "### Test body, banded (`Art/Impressions/test-body.png`)" in text
    assert "- Kind: artist's impression of Test Planet (NAIF ID 1001)." in text
    assert "- Reference: A test image, Test Mission, https://example.invalid/image" in text
    assert "- License: CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/)." in text
    assert "None yet" in "\n".join(attribution_section(()))


@pytest.mark.parametrize(
    "changes, message",
    [
        ({"kind": "sound"}, "unknown kind"),
        ({"origin": "found"}, "origin"),
        ({"license": ""}, "'license' is required"),
        ({"author": " "}, "'author' is required"),
        ({"file": "Textures/x.png"}, "under Art/"),
        ({"file": "Art/../x.png"}, "under Art/"),
        ({"license_url": "http://example.invalid/"}, "https"),
        ({"body": "1001"}, "NAIF ID"),
        ({"color": "red"}, "unknown keys"),
    ],
)
def test_rejects_bad_entries(changes, message):
    with pytest.raises(PipelineError, match=message):
        parse_artistic_assets(document(dict(ICON, **changes)))


@pytest.mark.parametrize(
    "changes, message",
    [
        ({"references": []}, "needs its references"),
        ({"body_name": ""}, "needs 'body' and 'body_name'"),
        ({"references": [{"title": "t", "url": "ftp://x/y", "credit": "c"}]}, "https"),
    ],
)
def test_an_artist_impression_needs_its_body_and_references(changes, message):
    with pytest.raises(PipelineError, match=message):
        parse_artistic_assets(document(dict(IMPRESSION, **changes)))


def test_rejects_a_file_listed_twice():
    with pytest.raises(PipelineError, match="listed twice"):
        parse_artistic_assets(document(ICON, ICON))


def test_every_file_in_the_art_folder_is_listed_and_every_listed_file_exists(tmp_path):
    repo = Repo(tmp_path)
    art = tmp_path / "UniverseSim" / "Assets" / "UniverseSim" / "Art" / "Icons"
    art.mkdir(parents=True)
    assets = parse_artistic_assets(document(ICON))
    with pytest.raises(PipelineError, match="does not exist"):
        check_files(repo, assets)
    (art / "test-icon.png").write_bytes(b"png")
    (art / "test-icon.png.meta").write_text("guid")  # Unity's .meta files are not assets
    check_files(repo, assets)
    (art / "unlisted.png").write_bytes(b"png")
    with pytest.raises(PipelineError, match="not listed"):
        check_files(repo, assets)


def test_the_section_is_rewritten_alone_and_the_rewrite_is_stable():
    maps = "# Attribution of visual assets\n\n## Mercury\n\n- Credit: test.\n"
    section = attribution_section(parse_artistic_assets(document(ICON)))
    once = splice_attribution(maps, section)
    assert once.startswith(maps)
    assert splice_attribution(once, section) == once
    empty = splice_attribution(once, attribution_section(()))
    assert empty.startswith(maps) and "Test icon" not in empty


def test_the_committed_attribution_lists_the_catalog():
    # The repository's catalog is valid, matches the Art folder, and ATTRIBUTION.md ends with its section (run
    # `usim-pipeline attribution` after editing the catalog).
    assets = load_artistic_assets(REAL_REPO)
    text = (REAL_REPO.root / ATTRIBUTION_PATH).read_text(encoding="utf-8")
    assert text == splice_attribution(text, attribution_section(assets))
