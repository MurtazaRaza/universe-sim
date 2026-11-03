"""Sky cues for the Unity viewer (A-07, B-06, ADR-0018): qualitative sky and twilight colors with their sources.

data/catalog/sky-cues.json lists, per body, a cue (the daytime sky, or the twilight), a hue from a fixed vocabulary
and the sentence of a registered source that states it. The pipeline validates the entries against
data/sources.json and writes the manifest the Unity host reads, each cue with its citation and tier. The hues are
facts; the colors that draw them are display choices of the host (docs/ART_DIRECTION.md section 5). Whether a body
has an atmosphere at all is not here: the core derives it from the pack's surface conditions.
"""

from __future__ import annotations

import datetime
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any

from . import PipelineError
from .jsonio import read_json, write_json
from .repo import Repo
from .sources import Source, load_sources

SCHEMA_VERSION = 1
CATALOG_NAME = "sky-cues.json"

# The cues and what they mean (ADR-0018 decision 2).
CUES = {
    "daytime_sky": "the hue of the sky and of daylight at the surface by day",
    "twilight": "the hue the atmosphere takes in the light of a low Sun: near the Sun seen from the surface, and along "
    "the terminator at the limb seen from space",
}

# The hues a cue may name. The host has a display color for each (a test there checks it).
HUES = ("blue", "red_and_yellow", "brownish_gray", "orange")

_REQUIRED_TEXT = ("body", "cue", "hue", "source_id", "url", "accessed", "reference", "quote")
_OPTIONAL_TEXT = ("note",)


@dataclass(frozen=True)
class SkyCue:
    """One sourced cue."""

    naif_id: int
    body: str
    cue: str
    hue: str
    source_id: str
    url: str
    accessed: str
    reference: str
    quote: str
    note: str


@dataclass(frozen=True)
class SkyCueCatalog:
    """The validated catalog: where the manifest goes (relative to the repository root) and the cues."""

    output: str
    cues: tuple[SkyCue, ...]


def parse_sky_cues(document: Any, sources: tuple[Source, ...], where: str = CATALOG_NAME) -> SkyCueCatalog:
    """Validates a parsed sky-cues.json document against the registered sources."""
    if not isinstance(document, dict) or document.get("schema_version") != SCHEMA_VERSION:
        raise PipelineError(f"{where}: expected an object with schema_version {SCHEMA_VERSION}")
    unknown = set(document) - {"schema_version", "description", "output", "cues"}
    if unknown:
        raise PipelineError(f"{where}: unknown keys {sorted(unknown)}")
    output = document.get("output")
    if not isinstance(output, str) or not output.endswith(".json"):
        raise PipelineError(f"{where}: 'output' must be the path of a .json file")
    path = PurePosixPath(output)
    if path.is_absolute() or ".." in path.parts or path.parts[:3] != ("UniverseSim", "Assets", "UniverseSim"):
        raise PipelineError(f"{where}: 'output' must lie under UniverseSim/Assets/UniverseSim/")
    entries = document.get("cues")
    if not isinstance(entries, list):
        raise PipelineError(f"{where}: 'cues' must be a list")
    declared = {s.id for s in sources}
    seen: set[tuple[int, str]] = set()
    cues: list[SkyCue] = []
    for index, entry in enumerate(entries):
        at = f"{where}: cues[{index}]"
        if not isinstance(entry, dict):
            raise PipelineError(f"{at}: must be an object")
        unknown = set(entry) - {"naif_id", *_REQUIRED_TEXT, *_OPTIONAL_TEXT}
        if unknown:
            raise PipelineError(f"{at}: unknown keys {sorted(unknown)}")
        naif_id = entry.get("naif_id")
        if isinstance(naif_id, bool) or not isinstance(naif_id, int):
            raise PipelineError(f"{at}: 'naif_id' must be an integer")
        for key in _REQUIRED_TEXT:
            if not isinstance(entry.get(key), str) or not entry[key].strip():
                raise PipelineError(f"{at}: '{key}' is required and must be non-empty")
        note = entry.get("note", "")
        if not isinstance(note, str):
            raise PipelineError(f"{at}: 'note' must be text")
        if entry["cue"] not in CUES:
            raise PipelineError(f"{at}: unknown cue {entry['cue']!r} (known: {', '.join(sorted(CUES))})")
        if entry["hue"] not in HUES:
            raise PipelineError(f"{at}: unknown hue {entry['hue']!r} (known: {', '.join(HUES)})")
        if entry["source_id"] not in declared:
            raise PipelineError(f"{at}: source {entry['source_id']} is not declared in sources.json")
        if not entry["url"].startswith("https://"):
            raise PipelineError(f"{at}: url must use https")
        try:
            datetime.date.fromisoformat(entry["accessed"])
        except ValueError:
            raise PipelineError(f"{at}: 'accessed' must be a date like 2026-11-02") from None
        key = (naif_id, entry["cue"])
        if key in seen:
            raise PipelineError(f"{at}: body {naif_id} has two '{entry['cue']}' cues")
        seen.add(key)
        cues.append(
            SkyCue(
                naif_id=naif_id,
                body=entry["body"],
                cue=entry["cue"],
                hue=entry["hue"],
                source_id=entry["source_id"],
                url=entry["url"],
                accessed=entry["accessed"],
                reference=entry["reference"],
                quote=entry["quote"],
                note=note,
            )
        )
    cues.sort(key=lambda c: (c.naif_id, c.cue))
    return SkyCueCatalog(output=output, cues=tuple(cues))


def unity_manifest(catalog: SkyCueCatalog, sources: tuple[Source, ...]) -> dict[str, Any]:
    """The manifest the Unity host reads (field names as its JsonUtility classes expect)."""
    tiers = {s.id: s.tier for s in sources}
    return {
        "generatedBy": f"tools/pipeline/usim-pipeline sky-cues from data/catalog/{CATALOG_NAME}. Do not edit by hand.",
        "cues": [
            {
                "naifId": cue.naif_id,
                "body": cue.body,
                "cue": cue.cue,
                "hue": cue.hue,
                "sourceId": cue.source_id,
                "tier": tiers[cue.source_id],
                "reference": cue.reference,
                "url": cue.url,
                "accessed": cue.accessed,
                "quote": cue.quote,
            }
            for cue in catalog.cues
        ],
    }


def load_sky_cues(repo: Repo) -> tuple[SkyCueCatalog, tuple[Source, ...]]:
    """Reads and validates data/catalog/sky-cues.json with the registered sources."""
    sources = load_sources(repo.sources_file)
    return parse_sky_cues(read_json(repo.catalog / CATALOG_NAME), sources), sources


def build_sky_cues(repo: Repo, log: Callable[[str], None] = print) -> Path:
    """Writes the Unity manifest of data/catalog/sky-cues.json and returns its path."""
    catalog, sources = load_sky_cues(repo)
    path = repo.root / Path(*PurePosixPath(catalog.output).parts)
    write_json(path, unity_manifest(catalog, sources))
    log(f"wrote {path} ({len(catalog.cues)} cues)")
    return path
