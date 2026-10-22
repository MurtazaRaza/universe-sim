"""Planetary rings (P6-04): data/catalog/rings.json, values transcribed from registered sources.

Each value is kept as the text on the source page (for example "74,490 - 91,980" or "10^-5 to 10^-3") and
parsed here, so the file shows exactly what was copied. Radii given as a center distance and a radial width
become center minus and plus half the width, and the locator records that derivation.

Format:
    {
      "schema_version": 1,
      "rings": [
        {
          "naif_id": 699, "name": "C Ring", "gap": false,
          "radii": {"text": "74,490 - 91,980", "source_id": ..., "url": ..., "accessed": "2026-10-22", "table": ...},
          "optical_depth": {"text": "0.05 to 0.35", "source_id": ..., "url": ..., "accessed": ..., "table": ...},
          "transcriber_note": "Who copied it and how it was checked."
        },
        {
          "naif_id": 699, "name": "Encke Gap", "gap": true,
          "radii": {"text": "133,570", "width_text": "325", "source_id": ..., ...},
          "transcriber_note": ...
        }
      ]
    }

A gap has no optical depth. "optical_depth" is optional for a ring.
"""

from __future__ import annotations

import datetime
import re
from fractions import Fraction
from pathlib import Path

from . import PipelineError
from .jsonio import read_json
from .pack.model import PackRing, naif
from .sources import Source

_NUMBER = r"\d{1,3}(?:,\d{3})*(?:\.\d+)?|\d+(?:\.\d+)?"
_RANGE = re.compile(rf"({_NUMBER})\s*-\s*({_NUMBER})")
_SINGLE = re.compile(rf"({_NUMBER})")
_POWER = r"10\^(-?\d+)"
_DEPTH_TERM = r"10\^-?\d+|\d+(?:\.\d+)?"  # no inner groups: _DEPTH numbers its own
_DEPTH = re.compile(rf"({_DEPTH_TERM})\s+to\s+({_DEPTH_TERM})")
_REFERENCE_KEYS = ("source_id", "url", "accessed", "table")


def _value(text: str) -> Fraction:
    power = re.fullmatch(_POWER, text)
    if power:
        return Fraction(10) ** int(power.group(1))
    return Fraction(text.replace(",", ""))


def parse_radii(text: str, width_text: str | None) -> tuple[float, float, str]:
    """(inner km, outer km, derivation) from "a - b", or from a center distance and a radial width."""
    if width_text is None:
        match = _RANGE.fullmatch(text.strip())
        if not match:
            raise PipelineError(f"radii {text!r}: expected 'inner - outer' (or a center with width_text)")
        return float(_value(match.group(1))), float(_value(match.group(2))), ""
    center, width = _SINGLE.fullmatch(text.strip()), _SINGLE.fullmatch(width_text.strip())
    if not center or not width:
        raise PipelineError(f"radii {text!r} with width {width_text!r}: expected one center and one width")
    c, w = _value(center.group(1)), _value(width.group(1))
    derivation = f"center {text.strip()} km and radial width {width_text.strip()} km: center minus and plus half the width"
    return float(c - w / 2), float(c + w / 2), derivation


def parse_optical_depth(text: str) -> tuple[float, float]:
    """(smallest, largest) from "a to b", where a term is a decimal or a power of ten written 10^n."""
    match = _DEPTH.fullmatch(text.strip())
    if not match:
        raise PipelineError(f"optical depth {text!r}: expected 'a to b'")
    return float(_value(match.group(1))), float(_value(match.group(2)))


def _reference(entry: object, where: str, declared: set[str], extra: tuple[str, ...] = ()) -> dict[str, str]:
    if not isinstance(entry, dict) or not set(_REFERENCE_KEYS) | {"text"} <= set(entry) <= set(_REFERENCE_KEYS) | {"text", *extra}:
        raise PipelineError(f"{where}: needs text, {', '.join(_REFERENCE_KEYS)}" + (f" and optionally {extra}" if extra else ""))
    if not all(isinstance(entry[k], str) and entry[k].strip() for k in entry):
        raise PipelineError(f"{where}: every field must be a non-empty string")
    if entry["source_id"] not in declared:
        raise PipelineError(f"{where}: source {entry['source_id']} is not declared in sources.json")
    if not entry["url"].startswith("https://"):
        raise PipelineError(f"{where}: url must use https")
    try:
        datetime.date.fromisoformat(entry["accessed"])
    except ValueError:
        raise PipelineError(f"{where}: 'accessed' must be a date like 2026-10-22") from None
    return entry


def _locator(reference: dict[str, str], derivation: str = "") -> str:
    text = f"{reference['table']}: \"{reference['text']}\""
    if "width_text" in reference:
        text += f", width \"{reference['width_text']}\""
    text += f" ({reference['url']}, accessed {reference['accessed']}; transcribed in data/catalog/rings.json)"
    return text + (f"; derived: {derivation}" if derivation else "")


def load_rings(path: Path, sources: tuple[Source, ...], selected: set[int]) -> list[PackRing]:
    """The rings of the selected bodies from a rings file."""
    document = read_json(path)
    if not isinstance(document, dict) or document.get("schema_version") != 1 or not isinstance(document.get("rings"), list):
        raise PipelineError(f"{path.name}: expected an object with schema_version 1 and a list 'rings'")
    declared = {s.id for s in sources}
    result: list[PackRing] = []
    for index, entry in enumerate(document["rings"]):
        where = f"{path.name}: rings[{index}]"
        keys = {"naif_id", "name", "gap", "radii", "transcriber_note"}
        if not isinstance(entry, dict) or not keys <= set(entry) <= keys | {"optical_depth"}:
            raise PipelineError(f"{where}: needs {sorted(keys)} and optionally optical_depth")
        naif_id, name, gap = entry["naif_id"], entry["name"], entry["gap"]
        if isinstance(naif_id, bool) or not isinstance(naif_id, int) or not isinstance(gap, bool):
            raise PipelineError(f"{where}: naif_id must be an integer and gap true or false")
        if not isinstance(name, str) or not name.strip() or not str(entry["transcriber_note"]).strip():
            raise PipelineError(f"{where}: name and transcriber_note are required")
        radii = _reference(entry["radii"], f"{where}: radii", declared, ("width_text",))
        inner, outer, derivation = parse_radii(radii["text"], radii.get("width_text"))
        depth = None
        depth_source = None
        depth_locator = ""
        if "optical_depth" in entry:
            if gap:
                raise PipelineError(f"{where}: a gap has no optical depth")
            reference = _reference(entry["optical_depth"], f"{where}: optical_depth", declared)
            depth = parse_optical_depth(reference["text"])
            depth_source = reference["source_id"]
            depth_locator = _locator(reference)
        if naif_id not in selected:
            continue
        result.append(
            PackRing(
                body=naif(naif_id), name=name, gap=gap, inner_km=inner, outer_km=outer,
                radii_source_id=radii["source_id"], radii_locator=_locator(radii, derivation),
                optical_depth=depth, optical_depth_source_id=depth_source, optical_depth_locator=depth_locator,
            )
        )  # fmt: skip
    names = [(r.body, r.name) for r in result]
    if len(set(names)) != len(names):
        raise PipelineError(f"{path.name}: a ring is listed twice")
    return result
